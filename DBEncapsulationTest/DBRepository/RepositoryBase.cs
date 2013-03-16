using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.Objects.DataClasses;
using System.Data;
using System.Data.EntityClient;
using System.Data.Metadata.Edm;
using System.Data.Mapping;
using System.Reflection;
using System.Collections;
using System.Threading;
using System.Data.Objects;
using System.Runtime.Serialization;
using System.Configuration;
using System.Data.Common;
using System.Linq.Expressions;

namespace DBRepository
{
    public delegate object EFCallBackHandler(ObjectContext em);
    public delegate TResult EFSelectCallBackHandler<IQueryable, TResult>(IQueryable query);

    /// <summary>
    /// 摘要：
    ///     Entity Framework封装类,简化访问
    ///     请不要随意修改这个类
    /// xxx.Config参考配置：
    ///     <configuration>
    ///        <appSettings>
    ///            <!--连接字符串的名称-->
    ///            <add key="ConnectionStringKey" value="modelfirstEntities"/>
    ///            <!--实体容器的名称-->
    ///            <add key="EntityContainerName" value="modelfirstEntities"/>
    ///            <!--对象上下文类型-->
    ///            <add key="ObjectContextType" value="EFLabModelFirst.modelfirstEntities"/>
    ///        </appSettings>
    ///      <connectionStrings>
    ///        <add name="modelfirstEntities" connectionString="metadata=res://*/ModelFirst.csdl|res://*/ModelFirst.ssdl|res://*/ModelFirst.msl;provider=System.Data.SqlClient;provider connection string="Data Source=.\sqlexpress;Initial Catalog=modelfirst;Integrated Security=True;MultipleActiveResultSets=True"" providerName="System.Data.EntityClient" />
    ///      </connectionStrings>
    ///     </configuration>
    /// </summary>
    public class RepositoryBase
    {
        [ThreadStatic]
        private static ObjectContext _em = null;
        /// <summary>
        /// 对象上下文,每个线程访问该属性可以获得一个单独的实例
        /// </summary>
        private static ObjectContext EM
        {
            get
            {
                if (_em == null)
                {
                    try
                    {
                        _em = Activator.CreateInstance(Type.GetType(ObjectContextType), ConfigurationManager.ConnectionStrings[ConnectionStringKey].ConnectionString, EntityContainerName) as ObjectContext;
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("创建ObjectContext失败,可能是在模型代码后置文件\"xxx.Designer.cs\"里面没有找到需要手动添加的构造函数,比如：public modelfirstEntities(string connectionString,string defaultContainerName):base(connectionString,defaultContainerName)", ex);
                    }
                    if (_em == null)
                        throw new Exception("创建ObjectContext失败，原因不明");
                }

                return _em;
            }
        }

        /// <summary>
        /// 连接字符串key
        /// </summary>
        static readonly string ConnectionStringKey = string.Empty;
        /// <summary>
        /// 实体容器名称
        /// </summary>
        static readonly string EntityContainerName = string.Empty;
        /// <summary>
        /// 对象上下文名称
        /// </summary>
        static readonly string ObjectContextType = string.Empty;

        /// <summary>
        /// 存储所有实体类名和实体集名称的映射
        /// Key:实体类型名称
        /// Value:实体集名称
        /// </summary>
        public static readonly Dictionary<String, String> EntitySetNameMap = new Dictionary<String, String>();

        /// <summary>
        /// 静态代码块,所有线程执行前该方法有且仅有执行一次
        /// </summary>
        static RepositoryBase()
        {
            ObjectContextType = ConfigurationManager.AppSettings["ObjectContextType"];
            ConnectionStringKey = ConfigurationManager.AppSettings["ConnectionStringKey"];
            EntityContainerName = ConfigurationManager.AppSettings["EntityContainerName"];
            if (string.IsNullOrEmpty(ObjectContextType))
                throw new Exception("xxx.Config文件\"appSettings节\"没有配置ObjectContext");
            if (string.IsNullOrEmpty(ConnectionStringKey))
                throw new Exception("xxx.Config文件\"connectionStrings节\"没有配置连接字符串");
            if (string.IsNullOrEmpty(EntityContainerName))
                throw new Exception("xxx.Config文件\"appSettings节\"没有配置实体容器名称");

            //构建连接对象
            EntityConnectionStringBuilder ecsb = new EntityConnectionStringBuilder(ConfigurationManager.ConnectionStrings[ConnectionStringKey].ConnectionString);
            string[] arrstr = ecsb.Metadata.Split('|');//得到映射文件
            EdmItemCollection e = new EdmItemCollection(arrstr[0]);//得到CSDL
            StoreItemCollection s = new StoreItemCollection(arrstr[1]);//得到SSDL
            StorageMappingItemCollection smt = new StorageMappingItemCollection(e, s, arrstr[2]);//得到MSL
            //获取合部实体对应关系
            var entities = smt[0].GetType().GetProperty("EntitySetMaps", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(smt[0], null);
            //反射得到StorageSetMapping类型
            Assembly ass = Assembly.Load("System.Data.Entity,Version=3.5.0.0,culture=Neutral,PublicKeyToken=b77a5c561934e089");
            Type type = ass.GetType("System.Data.Mapping.StorageSetMapping");
            foreach (var entityvalue in (IList)entities)
            {
                var p = entityvalue.GetType().GetField("m_extent", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                EntitySet es = (EntitySet)type.GetField("m_extent", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entityvalue);
                EntitySetNameMap.Add(es.ElementType.Name, es.Name);
            }
        }

        /// <summary>
        /// 提交保存
        /// </summary>
        public void Commit()
        {
            EM.SaveChanges();
        }

        /// <summary>
        /// 获得一个实体的主键名称
        /// </summary>
        /// <param name="entityType">实体类的类型信息对象</param>
        /// <returns>主键名称</returns>
        public virtual string GetKeyName(Type entityType)
        {
            foreach (var pi in entityType.GetProperties())
            {
                var attr = pi.GetCustomAttributes(typeof(EdmScalarPropertyAttribute), false).FirstOrDefault()
                    as EdmScalarPropertyAttribute;
                if (attr != null &&
                    pi.GetCustomAttributes(typeof(DataMemberAttribute), false).FirstOrDefault() != null &&
                    attr.EntityKeyProperty)
                    return pi.Name;
            }
            return string.Empty;
        }


        /// <summary>
        /// 添加一个对象
        /// </summary>
        /// <param name="obj">待添加的对象</param>
        public virtual void AddObject(Object obj)
        {
            EM.AddObject(EntitySetNameMap[obj.GetType().Name], obj);
        }

        /// <summary>
        /// 删除一个对象
        /// </summary>
        /// <param name="obj">待删除的对象</param>
        public virtual void DeleteObject(Object obj)
        {
            EM.AttachTo(EntitySetNameMap[obj.GetType().Name], obj);
            EM.DeleteObject(obj);
        }

        /// <summary>
        /// 获得一个实体
        /// </summary>
        /// <typeparam name="T">对象类型</typeparam>
        /// <param name="keyValue">主键值</param>
        /// <returns>查询到的对象</returns>
        public virtual T GetEntityByKey<T>(object keyValue) where T : class
        {
            return
                EM.GetObjectByKey(new EntityKey(EntityContainerName + "." + EntitySetNameMap[typeof(T).Name], GetKeyName(typeof(T)), keyValue)) as T;
        }

        /// <summary>
        /// 根据条件获取某个对象
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="where">查询条件</param>
        /// <returns>实体</returns>
        public virtual T GetEntity<T>(Expression<Func<T, bool>> where) where T : class
        {
            return EM.CreateObjectSet<T>().Where(where).FirstOrDefault();
        }

        /// <summary>
        /// 该函数已过期,根据条件获取某个值,比如聚合函数查询,
        /// </summary>
        /// <typeparam name="T">类型</typeparam>
        /// <typeparam name="TResult">查询结果了性</typeparam>
        /// <param name="where">查询条件</param>
        /// <param name="selector">投影表达式树</param>
        /// <returns></returns>
        //public virtual TResult GetObject<T, TResult>(Expression<Func<T, bool>> where, Expression<Func<T, TResult>> selector) where T : class
        //{
        //    return EM.CreateObjectSet<T>().Where<T>(where).Select<T, TResult>(selector).FirstOrDefault<TResult>();
        //}

        /// <summary>
        /// 根据条件获取某个值,比如聚合函数查询
        /// </summary>
        /// <typeparam name="T">被查询的类型</typeparam>
        /// <typeparam name="TResult">查询结果类型</typeparam>
        /// <param name="where">查询条件</param>
        /// <param name="includes">要抓取的导航属性数组</param>
        /// <param name="selectHandler">投影委托</param>
        /// <returns>查询结果</returns>
        public virtual TResult GetObject<T, TResult>(EFSelectCallBackHandler<IQueryable<T>, TResult> selectHandler, Expression<Func<T, bool>> where, params string[] includes) where T : class
        {
            if (selectHandler == null)
                throw new Exception("委托EFSelectCallBackHandler不能为空");
            IQueryable<T> query = EM.CreateObjectSet<T>();
            if (includes == null || includes.Length == 0)
                query = query.Where<T>(where == null ? t => true : where);
            else
            {
                foreach (var include in includes)
                {
                    if (string.IsNullOrEmpty(include))
                        continue;
                    query = (query as ObjectQuery<T>).Include(include);
                }
            }

            return selectHandler(query);
        }

        /// <summary>
        /// 根据条件获取某个值,比如聚合函数查询
        /// </summary>
        /// <typeparam name="T">被查询的类型</typeparam>
        /// <typeparam name="TResult">查询结果类型</typeparam>
        /// <param name="includes">要抓取的导航属性数组</param>
        /// <param name="selectHandler">投影委托</param>
        /// <returns>查询结果</returns>
        public virtual TResult GetObject<T, TResult>(EFSelectCallBackHandler<IQueryable<T>, TResult> selectHandler, params string[] includes) where T : class
        {
            return GetObject<T, TResult>(selectHandler, null, includes);
        }

        /// <summary>
        /// 根据实体的实体键修改实体
        /// </summary>
        /// <param name="obj"></param>
        public virtual void UpdateObject(object obj)
        {
            EM.AttachTo(EntitySetNameMap[obj.GetType().Name], obj);
            EM.ObjectStateManager.ChangeObjectState(obj, EntityState.Modified);
        }

        /// <summary>
        /// 根据条件进行查询
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="where">查询条件</param>
        /// <returns>符号条件的实体的集合</returns>
        public ICollection<T> FindList<T>(Func<T, bool> where) where T : class
        {
            return EM.CreateObjectSet<T>().Where<T>(where).AsEnumerable().ToList();
        }
        /// <summary>
        /// 根据多个条件进行查询
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="where">查询条件</param>
        /// <returns>符合条件的实体的集合</returns>
        public ICollection<T> FindList<T>(params Func<T, bool>[] where) where T : class
        {
            if (where != null && where.Length > 0)
            {
                IEnumerable<T> query = EM.CreateObjectSet<T>().Where(where[0]);
                for (int i = 1; i < where.Length; i++)
                {
                    query = query.Where(where[i]);
                }
                return query.ToList<T>();
            }
            return null;
        }

        /// <summary>
        /// 根据条件、排序、投影进行查找
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <typeparam name="TKey">排序属性类型</typeparam>
        /// <typeparam name="TResult">返回结果类型</typeparam>
        /// <param name="where">查询条件树</param>
        /// <param name="orderSelector">排序</param>
        /// <param name="selector">投影</param>
        /// <returns>符合条件的对象的集合</returns>
        public ICollection<TResult> FindList<T, TKey, TResult>(Expression<Func<T, bool>> where, Func<T, TKey> orderSelector, Func<T, TResult> selector) where T : class
        {
            return EM.CreateObjectSet<T>().Where<T>(where).OrderBy(orderSelector).Select(selector).ToList();
        }

        /// <summary>
        /// 根据条件和排序进行查找,带抓取功能
        /// </summary>
        /// <typeparam name="T">被查询的实体类型</typeparam>
        /// <typeparam name="TKey">排序属性的类型</typeparam>
        /// <typeparam name="TResult">查询结果类型</typeparam>
        /// <param name="where">查询条件</param>
        /// <param name="include">抓取属性</param>
        /// <param name="orderSelector">排序选择器</param>
        /// <param name="selector">投影选择器</param>
        /// <returns></returns>
        public ICollection<TResult> FindList<T, TKey, TResult>(Expression<Func<T, bool>> where, string include, Func<T, TKey> orderSelector, Func<T, TResult> selector) where T : class
        {
            return EM.CreateObjectSet<T>().Include(include).Where<T>(where).OrderBy(orderSelector).Select(selector).ToList();
        }

        /// <summary>
        ///  执行原始SQL查询
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="commandText">要执行查询的SQL语句</param>
        /// <param name="parameters">参数</param>
        /// <returns>符合条件的对象的集合</returns>
        public ICollection<T> ExecuteSqlQuery<T>(string commandText, params DbParameter[] parameters)
        {
            return
                EM.ExecuteStoreQuery<T>(commandText, parameters).ToList<T>();
        }

        /// <summary>
        /// 执行原始SQL命令
        /// </summary>
        /// <param name="commandText">SQL命令</param>
        /// <param name="parameters">参数</param>
        /// <returns>影响的记录数</returns>
        public int ExecuteSqlNonQuery(string commandText, params DbParameter[] parameters)
        {

            return
                EM.ExecuteStoreCommand(commandText, parameters);
        }

        /// <summary>
        /// 执行任何操作
        /// </summary>
        /// <param name="callBackHandler"></param>
        /// <returns></returns>
        protected object Execute(EFCallBackHandler callBackHandler)
        {
            return callBackHandler(EM);
        }
        /// <summary>
        /// 分页泛型方法
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="Tkey"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="pi"></param>
        public void Pager<T, Tkey, TResult>(PageInfo<T, Tkey, TResult> pi) where T : class
        {
            ObjectSet<T> os = EM.CreateObjectSet<T>();

            IQueryable<T> query = null;
            if (!string.IsNullOrEmpty(pi.Include))
                query = os.Include(pi.Include);
            else
                query = os.AsQueryable<T>();

            query = query.Where<T>(pi.Where == null ? t => true : pi.Where);

            //总条数
            pi.RecordCount = query.LongCount();
            //总页数
            pi.PageCount = pi.RecordCount % pi.PageSize == 0 ? pi.RecordCount / pi.PageSize : pi.RecordCount / pi.PageSize + 1;

            query = query
                    .OrderBy<T, Tkey>(pi.Order == null ? t => default(Tkey) : pi.Order)
                    .Skip<T>((pi.PageIndex - 1) * pi.PageSize)
                    .Take<T>(pi.PageSize);
            if (pi.Select != null)
                pi.List = query.Select<T, TResult>(pi.Select).ToList<TResult>();
            else
                pi.List = query.ToList<T>() as ICollection<TResult>;

        }

    }
}
