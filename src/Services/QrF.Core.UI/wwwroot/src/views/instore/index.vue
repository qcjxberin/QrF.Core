<template>
  <div class="app-container calendar-list-container">
    <div class="filter-container">
      <el-form :model="q" ref="q" label-width="110px" class="el-form">
            <el-row :gutter="10">
                <cl label="标题" code="title">
                    <el-input @keyup.enter.native="handleFilter" class="filter-item" placeholder="标题" v-model="q.title"></el-input>
                </cl>
                <cl label="状态" code="status">
                    <el-select @change='handleFilter' clearable class="filter-item" v-model="q.status" placeholder="状态">
                      <el-option v-for="item in statusOptions" :key="item.id" :label="item.text" :value="item.id"></el-option>
                    </el-select>
                </cl>
                <el-col :xs="{span: 12}" :sm="{span: 8, offset: 0}" :md="{span: 6, offset: 6}">
                  <el-button class="filter-item" style="float:right;" @click="handleUpdate(null,'create')" type="success" v-waves icon="edit">添加</el-button>
                    <el-button icon="icon-zhongzhi" :plain="true" @@click="resetForm('q')" style="float:right;margin-right:10px;">重置</el-button>
                    <el-button class="filter-item" type="primary" icon="search" v-waves @@click="handleFilter" style="float:right;">查询</el-button>
                </el-col>
            </el-row>
        </el-form>
    </div>

    <el-table :key='tableKey' :data="list" v-loading="listLoading" @row-dblclick="handleUpdate($event,null)" border fit highlight-current-row style="width: 100%">

      <el-table-column align="center" label="序号" width="65">
        <template slot-scope="scope">
          <span>{{scope.row.id}}</span>
        </template>
      </el-table-column>

      <el-table-column width="180px" align="center" label="时间">
        <template slot-scope="scope">
          <span>{{scope.row.timestamp | parseTime('{y}-{m}-{d} {h}:{i}')}}</span>
        </template>
      </el-table-column>

      <el-table-column min-width="300px" label="标题">
        <template slot-scope="scope">
          <span>{{scope.row.title}}</span>
        </template>
      </el-table-column>

      <el-table-column width="110px" align="center" label="作者">
        <template slot-scope="scope">
          <span>{{scope.row.author}}</span>
        </template>
      </el-table-column>

      <el-table-column width="110px" v-if='showAuditor' align="center" label="审核人">
        <template slot-scope="scope">
          <span style='color:red;'>{{scope.row.auditor}}</span>
        </template>
      </el-table-column>

      <el-table-column width="80px" label="重要性">
        <template slot-scope="scope">
          <icon-svg v-for="n in +scope.row.importance" icon-class="star" class="meta-item__icon" :key="n"></icon-svg>
        </template>
      </el-table-column>

      <el-table-column align="center" label="阅读数" width="95">
        <template slot-scope="scope">
          <span class="link-type">{{scope.row.pageviews}}</span>
        </template>
      </el-table-column>

      <el-table-column class-name="status-col" label="状态" width="90">
        <template slot-scope="scope">
          <el-tag :type="scope.row.status | statusFilter">{{scope.row.status | enumName(statusOptions)}}</el-tag>
        </template>
      </el-table-column>

      <el-table-column align="center" label="操作" width="150">
        <template slot-scope="scope">
          <el-button v-if="scope.row.status!=3" size="small" type="success" @click="handleUpdate(scope.row)">编辑
          </el-button>
          <el-button v-if="scope.row.status==3" size="small" @click="handleUpdate(scope.row)">查看
          </el-button>
          <el-button v-if="scope.row.status!=3" size="small" type="danger" @click="handleDelete(scope.row)">删除
          </el-button>
        </template>
      </el-table-column>

    </el-table>

    <div v-show="!listLoading" class="pagination-container">
      <el-pagination @size-change="handleSizeChange" @current-change="handleCurrentChange" :current-page.sync="q.page"
        :page-sizes="[10,20,30, 50]" :page-size="q.limit" layout="total, sizes, prev, pager, next, jumper" :total="total">
      </el-pagination>
    </div>

    <el-dialog :title="textMap[dialogStatus]" :visible.sync="dialogFormVisible">
      <el-form class="small-space" :model="temp" ref="temp" label-position="left" label-width="70px" style='width: 400px; margin-left:50px;'>

        <el-form-item label="状态">
          <el-select class="filter-item" v-model="temp.status" placeholder="请选择">
            <el-option v-for="item in  statusOptions" :key="item.id" :label="item.text" :value="item.id">
            </el-option>
          </el-select>
        </el-form-item>

        <el-form-item label="时间">
          <el-date-picker v-model="temp.timestamp" type="datetime" placeholder="选择日期时间">
          </el-date-picker>
        </el-form-item>

        <el-form-item label="标题">
          <el-input v-model="temp.title"></el-input>
        </el-form-item>

        <el-form-item label="点评">
          <el-input type="textarea" :autosize="{ minRows: 2, maxRows: 4}" placeholder="请输入内容" v-model="temp.remark">
          </el-input>
        </el-form-item>
      </el-form>
      <div slot="footer" class="dialog-footer">
        <el-button @click="dialogFormVisible = false">取 消</el-button>
        <el-button v-if="dialogStatus=='create'" type="primary" @click="create">确 定</el-button>
        <el-button v-if="dialogStatus=='update' && temp.status !=3" type="primary" @click="update">确 定</el-button>
      </div>
    </el-dialog>
  </div>
</template>

<script>
import cl from '@/components/Form'
import { fetchList } from '@/api/godownEntry'

export default {
  name: 'table_demo',
  components: {
    cl
  },
  data() {
    return {
      list: null,
      total: null,
      listLoading: true,
      q: {
        page: 1,
        limit: 10,
        importance: undefined,
        title: undefined,
        status: undefined
      },
      temp: {
        id: undefined,
        importance: 0,
        remark: '',
        timestamp: 0,
        title: '',
        type: '',
        status: 1
      },
      statusOptions: [{ text: '已保存', spell: null, id: 1 }, { text: '已提交', spell: null, id: 2 }, { text: '审核通过', spell: null, id: 3 }],
      dialogFormVisible: false,
      dialogStatus: '',
      textMap: {
        update: '编辑',
        create: '创建'
      },
      showAuditor: false,
      tableKey: 0
    }
  },
  filters: {
    statusFilter(status) {
      return ['success', 'primary', 'gray', 'warning'][status]
    }
  },
  created() {
    this.getList()
  },
  methods: {
    getList() {
      this.listLoading = true
      fetchList(this.q).then(response => {
        this.list = response.data.items
        this.total = response.data.total
        this.listLoading = false
      })
    },
    handleFilter() {
      this.q.page = 1
      this.getList()
    },
    handleSizeChange(val) {
      this.q.limit = val
      this.getList()
    },
    handleCurrentChange(val) {
      this.q.page = val
      this.getList()
    },
    resetForm(formName) {
      if (this.$refs[formName]) { this.$refs[formName].resetFields() }
    },
    handleUpdate(row, status) {
      status = status || 'update'
      if (!row || status === 'create') this.resetTemp()
      else this.temp = Object.assign({}, row)
      this.dialogStatus = status
      this.dialogFormVisible = true
    },
    handleDelete(row) {
      this.$notify({
        title: '成功',
        message: '删除成功',
        type: 'success',
        duration: 2000
      })
      const index = this.list.indexOf(row)
      this.list.splice(index, 1)
    },
    create() {
      this.temp.id = parseInt(Math.random() * 100) + 1024
      this.temp.timestamp = +new Date()
      this.temp.author = '原创作者'
      this.list.unshift(this.temp)
      this.dialogFormVisible = false
      this.$notify({
        title: '成功',
        message: '创建成功',
        type: 'success',
        duration: 2000
      })
    },
    update() {
      this.temp.timestamp = +this.temp.timestamp
      for (const v of this.list) {
        if (v.id === this.temp.id) {
          const index = this.list.indexOf(v)
          this.list.splice(index, 1, this.temp)
          break
        }
      }
      this.dialogFormVisible = false
      this.$notify({
        title: '成功',
        message: '更新成功',
        type: 'success',
        duration: 2000
      })
    },
    resetTemp() {
      this.temp = {
        id: undefined,
        importance: 0,
        remark: '',
        timestamp: 0,
        title: '',
        status: 1,
        type: ''
      }
    }
  }
}
</script>
