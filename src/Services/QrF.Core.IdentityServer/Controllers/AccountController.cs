﻿using IdentityModel;
using IdentityServer4.Events;
using IdentityServer4.Extensions;
using IdentityServer4.Models;
using IdentityServer4.Services;
using IdentityServer4.Stores;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using QrF.Core.IdentityServer.Entities;
using QrF.Core.IdentityServer.Filters;
using QrF.Core.IdentityServer.Models;
using QrF.Core.IdentityServer.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace QrF.Core.IdentityServer.Controllers
{
    [SecurityHeaders]
    public class AccountController : Controller
    {
        private readonly AccountOptions _accountOptions;
        private readonly ILoginService<ApplicationUser> _loginService;
        private readonly IIdentityServerInteractionService _interaction;
        private readonly IEventService _events;
        private readonly AccountService _account;

        public AccountController(
            IOptions<AccountOptions> accountOptionsAccessor,
            ILoginService<ApplicationUser> loginService,
            IIdentityServerInteractionService interaction,
            IClientStore clientStore,
            IHttpContextAccessor httpContextAccessor,
            IAuthenticationSchemeProvider schemeProvider,
            IEventService events)
        {
            _accountOptions = accountOptionsAccessor.Value;
            _loginService = loginService;
            _interaction = interaction;
            _events = events;
            _account = new AccountService(accountOptionsAccessor,interaction, httpContextAccessor, schemeProvider, clientStore);
        }

        /// <summary>
        /// Show login page
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Login(string returnUrl)
        {
            // build a model so we know what to show on the login page
            var vm = await _account.BuildLoginViewModelAsync(returnUrl);

            if (vm.IsExternalLoginOnly)
            {
                // we only have one option for logging in and it's an external provider
                return await ExternalLogin(vm.ExternalLoginScheme, returnUrl);
            }

            return View(vm);
        }

        /// <summary>
        /// Handle postback from username/password login
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginInputModel model, string button)
        {
            if (button != "login")
            {
                // the user clicked the "cancel" button
                var context = await _interaction.GetAuthorizationContextAsync(model.ReturnUrl);
                if (context != null)
                {
                    // if the user cancels, send a result back into IdentityServer as if they 
                    // denied the consent (even if this client does not require consent).
                    // this will send back an access denied OIDC error response to the client.
                    await _interaction.GrantConsentAsync(context, ConsentResponse.Denied);

                    // we can trust model.ReturnUrl since GetAuthorizationContextAsync returned non-null
                    return Redirect(model.ReturnUrl);
                }
                else
                {
                    // since we don't have a valid context, then we just go back to the home page
                    return Redirect("~/");
                }
            }

            if (ModelState.IsValid)
            {
                var user = await _loginService.FindByUsername(model.Username);
                if (await _loginService.ValidateCredentials(user, model.Password))
                {
                    await _events.RaiseAsync(new UserLoginSuccessEvent(user.UserName, user.Id, user.Name));

                    // only set explicit expiration here if user chooses "remember me". 
                    // otherwise we rely upon expiration configured in cookie middleware.
                    AuthenticationProperties props = null;
                    if (_accountOptions.AllowRememberLogin && model.RememberLogin)
                    {
                        props = new AuthenticationProperties
                        {
                            IsPersistent = true,
                            ExpiresUtc = DateTimeOffset.UtcNow.Add(_accountOptions.RememberMeLoginDuration)
                        };
                    };
                    // issue authentication cookie with subject ID and username
                    await HttpContext.SignInAsync(user.Id, user.UserName, props);

                    // make sure the returnUrl is still valid, and if so redirect back to authorize endpoint or a local page
                    if (_interaction.IsValidReturnUrl(model.ReturnUrl) || Url.IsLocalUrl(model.ReturnUrl))
                    {
                        return Redirect(model.ReturnUrl);
                    }

                    return Redirect("~/");
                }

                await _events.RaiseAsync(new UserLoginFailureEvent(model.Username, "invalid credentials"));

                ModelState.AddModelError("", _accountOptions.InvalidCredentialsErrorMessage);
            }

            // something went wrong, show form with error
            var vm = await _account.BuildLoginViewModelAsync(model);
            return View(vm);
        }

        /// <summary>
        /// initiate roundtrip to external authentication provider
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ExternalLogin(string provider, string returnUrl)
        {
            var props = new AuthenticationProperties()
            {
                RedirectUri = Url.Action("ExternalLoginCallback"),
                Items =
                {
                    { "returnUrl", returnUrl }
                }
            };

            // windows authentication needs special handling
            // since they don't support the redirect uri, 
            // so this URL is re-triggered when we call challenge
            if (_accountOptions.WindowsAuthenticationSchemeName == provider)
            {
                // see if windows auth has already been requested and succeeded
                var result = await HttpContext.AuthenticateAsync(_accountOptions.WindowsAuthenticationSchemeName);
                if (result?.Principal is WindowsPrincipal wp)
                {
                    props.Items.Add("scheme", _accountOptions.WindowsAuthenticationSchemeName);

                    var id = new ClaimsIdentity(provider);
                    id.AddClaim(new Claim(JwtClaimTypes.Subject, wp.Identity.Name));
                    id.AddClaim(new Claim(JwtClaimTypes.Name, wp.Identity.Name));

                    // add the groups as claims -- be careful if the number of groups is too large
                    if (_accountOptions.IncludeWindowsGroups)
                    {
                        var wi = wp.Identity as WindowsIdentity;
                        var groups = wi.Groups.Translate(typeof(NTAccount));
                        var roles = groups.Select(x => new Claim(JwtClaimTypes.Role, x.Value));
                        id.AddClaims(roles);
                    }

                    await HttpContext.SignInAsync(
                        IdentityServer4.IdentityServerConstants.ExternalCookieAuthenticationScheme,
                        new ClaimsPrincipal(id),
                        props);
                    return Redirect(props.RedirectUri);
                }
                else
                {
                    // challenge/trigger windows auth
                    return Challenge(_accountOptions.WindowsAuthenticationSchemeName);
                }
            }
            else
            {
                // start challenge and roundtrip the return URL
                props.Items.Add("scheme", provider);
                return Challenge(props, provider);
            }
        }

        /// <summary>
        /// Show logout page
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Logout(string logoutId)
        {
            // build a model so the logout page knows what to display
            var vm = await _account.BuildLogoutViewModelAsync(logoutId);

            if (vm.ShowLogoutPrompt == false)
            {
                // if the request for logout was properly authenticated from IdentityServer, then
                // we don't need to show the prompt and can just log the user out directly.
                return await Logout(vm);
            }

            return View(vm);
        }

        /// <summary>
        /// Handle logout page postback
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout(LogoutInputModel model)
        {
            // build a model so the logged out page knows what to display
            var vm = await _account.BuildLoggedOutViewModelAsync(model.LogoutId);

            var user = HttpContext.User;
            if (user?.Identity.IsAuthenticated == true)
            {
                // delete local authentication cookie
                await HttpContext.SignOutAsync();

                // raise the logout event
                await _events.RaiseAsync(new UserLogoutSuccessEvent(user.GetSubjectId(), user.GetName()));
            }

            // check if we need to trigger sign-out at an upstream identity provider
            if (vm.TriggerExternalSignout)
            {
                // build a return URL so the upstream provider will redirect back
                // to us after the user has logged out. this allows us to then
                // complete our single sign-out processing.
                string url = Url.Action("Logout", new { logoutId = vm.LogoutId });

                // this triggers a redirect to the external provider for sign-out
                return SignOut(new AuthenticationProperties { RedirectUri = url }, vm.ExternalAuthenticationScheme);
            }

            return View("LoggedOut", vm);
        }
    }
}
