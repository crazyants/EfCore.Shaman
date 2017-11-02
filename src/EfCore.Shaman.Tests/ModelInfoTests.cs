﻿#region using

using System;
using System.Data.SqlClient;
using System.Linq;
using System.Runtime.CompilerServices;
using EfCore.Shaman.ModelScanner;
using EfCore.Shaman.Tests.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

#endregion

namespace EfCore.Shaman.Tests
{
    public partial class ModelInfoTests
    {
        #region Static Methods

        static string GetSqlConnectionString(string memberName)
        {
            var cfg = TestsConfig.Load();
            var csb = new SqlConnectionStringBuilder(cfg.ConnectionStringTemplate);
            csb.InitialCatalog += memberName;
            var result = csb.ConnectionString;
            var dbName = csb.InitialCatalog;
            {
                if (SqlUtils.DbExists(result))
                {
                    SqlUtils.DropDb(result);
                }
                SqlUtils.CreateDb(result);
            }
            return result;
        }

        internal static void DoTestOnModelBuilder<T>(bool useSql, Action<ModelBuilder> checkMethod, Action<T> checkContext = null, [CallerMemberName]string memberName = null) where T : VisitableDbContext
        {
            var optionsBuilder = new DbContextOptionsBuilder<T>();
            optionsBuilder = useSql
                ? optionsBuilder.UseSqlServer(GetSqlConnectionString(memberName))
                : optionsBuilder.UseInMemoryDatabase(nameof(T02_ShouldHaveUniqueIndex));
            var options = optionsBuilder.Options;
            var count = 0;
            using (var context = InstanceCreator.CreateInstance<T>(EmptyShamanLogger.Instance, options))
            {
                context.ExternalCheckModel = b =>
                {
                    count++;
                    checkMethod?.Invoke(b);
                };
                if (useSql)
                {
                    Console.WriteLine("Migration");
                    context.Database.Migrate();
                }
                // var tmp = context.Settings.ToArray(); // enforce to create model
                var model = context.Model;
                if (model == null) // enforce to create model
                    throw new NullReferenceException();
  
                if (checkContext != null)
                    checkContext(context);
            }
            if (count == 0)
                throw new Exception("checkMethod has not been invoked");
        }

        internal static ModelInfo GetModelInfo<T>(ShamanOptions options = null)
        {
            var aa = new ModelInfo(typeof(T), options);
            return aa;
        }

        #endregion

        #region Instance Methods

        [Fact]
        public void T01_ShouldRecoginzeNotNull()
        {
            Assert.True(ModelInfo.NotNullFromPropertyType(typeof(int)));
            Assert.True(ModelInfo.NotNullFromPropertyType(typeof(long)));
            Assert.True(ModelInfo.NotNullFromPropertyType(typeof(Guid)));

            Assert.False(ModelInfo.NotNullFromPropertyType(typeof(int?)));
            Assert.False(ModelInfo.NotNullFromPropertyType(typeof(long?)));
            Assert.False(ModelInfo.NotNullFromPropertyType(typeof(Guid?)));

            Assert.False(ModelInfo.NotNullFromPropertyType(typeof(string)));
        }

        [Fact]
        public void T02_ShouldHaveUniqueIndex()
        {
            // todo: xunit tests (each test in separate appdomain). DbContext creates Model only once  
            DoTestOnModelBuilder<TestDbContext>(false, mb =>
            {
                var modelInfo = GetModelInfo<TestDbContext>();
                var dbSet = modelInfo.DbSet<MyEntityWithUniqueIndex>();
                Assert.NotNull(dbSet);
                var idxs = JsonHelper.SerializeToTest(dbSet.Indexes);
                const string expected = "[{'IndexName':'','Fields':[{'FieldName':'Name'}],'IndexType':'UniqueIndex'}]";
                Assert.Equal(expected, idxs);
            });
        }

        [Fact]
        public void T03_ShouldHaveManuallyChangedTableName()
        {
            DoTestOnModelBuilder<TestDbContext>(false, mb =>
            {
                var modelInfo = GetModelInfo<TestDbContext>();
                var dbSet = modelInfo.DbSet<MyEntityWithDifferentTableName>();
                Assert.NotNull(dbSet);
                Assert.Equal("ManualChange", dbSet.TableName);
            });
        }


        [Fact]
        public void T04_ShouldHaveEmptyService()
        {
            var services = ShamanOptions.CreateShamanOptions(typeof(TestDbContext)).Services;
            var cnt = services.Count(a => a is EmptyService);
            Assert.Equal(1, cnt);
        }

        [Fact]
        public void T05_ShouldHaveDefaultSchema()
        {
            var mi = ModelInfo.Make<TestDbContext>();
            Assert.Equal("testSchema", mi.DefaultSchema);
        }

        [Fact]
        public void T06_ShouldHaveTableNameWithPrefix()
        {
            const string expectedTableName = "myPrefixUsers";

            DoTestOnModelBuilder<PrefixedTableNamesDbContext>(true, mb =>
            {
                var t = mb.Model.GetEntityTypes().Single(a => a.ClrType == typeof(User));
                Assert.NotNull(t);
                Assert.Equal(expectedTableName, t.Relational().TableName);

            // without patching
            {
                    var modelInfo = GetModelInfo<PrefixedTableNamesDbContext>(ShamanOptions.Default);
                    var dbSet = modelInfo.DbSet<User>();
                    Assert.NotNull(dbSet);
                    Assert.Equal("Users", dbSet.TableName);
                }
            // with patching
            {
                    var modelInfo = GetModelInfo<PrefixedTableNamesDbContext>();
                    var dbSet = modelInfo.DbSet<User>();
                    Assert.NotNull(dbSet);
                    Assert.Equal(expectedTableName, dbSet.TableName);
                }
            });

            {
                var mi = ModelInfo.Make<PrefixedTableNamesDbContext>();
                var dbSet = mi.DbSets.Single(a => a.EntityType == typeof(User));
                Assert.Equal(expectedTableName, dbSet.TableName);
            }
        }

        [Fact]
        public void T07_ShouldHaveDefaultValue()
        {
            DoTestOnModelBuilder<TestDbContext>(false, mb =>
            {
                var modelInfo = GetModelInfo<TestDbContext>();
                var dbSet = modelInfo.DbSet<MyEntityWithDifferentTableName>();
                Assert.NotNull(dbSet);
                var col = dbSet.Properites.SingleOrDefault(a => a.ColumnName == "ElevenDefaultValue");
                Assert.NotNull(col);
                Assert.NotNull(col.DefaultValue);
                Assert.Equal(ValueInfoKind.Clr, col.DefaultValue.Kind);
                Assert.Equal(11, col.DefaultValue.ClrValue);
            });
        }


        [Fact]
        public void T08_ShouldHaveSqlDefaultValue()
        {
            DoTestOnModelBuilder<TestDbContext>(false, mb =>
            {
                var modelInfo = GetModelInfo<TestDbContext>();
                var dbSet = modelInfo.DbSet<MyEntityWithDifferentTableName>();
                Assert.NotNull(dbSet);
                var col = dbSet.Properites.SingleOrDefault(a => a.ColumnName == "NoneDefaultSqlValue");
                Assert.NotNull(col);
                Assert.NotNull(col.DefaultValue);
                Assert.Equal(ValueInfoKind.Sql, col.DefaultValue.Kind);
                Assert.Equal("NONE123", col.DefaultValue.SqlValue);
            });
        }

        [Fact]
        public void T09_ShouldHaveFullTextIndex()
        {
            // todo: xunit tests (each test in separate appdomain). DbContext creates Model only once  
            DoTestOnModelBuilder<TestDbContext>(false, mb =>
            {
                var modelInfo = GetModelInfo<TestDbContext>();
                var dbSet = modelInfo.DbSet<MyEntityWithFullTextIndex>();
                Assert.NotNull(dbSet);
                var idxs = JsonHelper.SerializeToTest(dbSet.Indexes);
                const string expected =
                    "[{'IndexName':'','Fields':[{'FieldName':'Name'}],'IndexType':'FullTextIndex','FullTextCatalogName':'my catalog'}]";
                Assert.Equal(expected, idxs);
            });
        }

        [Fact]
        public void T10_ShouldHaveSingularTableNames()
        {
            const string expectedTableName = "User";
            DoTestOnModelBuilder<SingularTableNamesDbContext>(false, mb =>
            {
                var t = mb.Model.GetEntityTypes().Single(a => a.ClrType == typeof(User));
                Assert.NotNull(t);
                Assert.Equal(expectedTableName, t.Relational().TableName);

            // without patching - when use 'InMemory' table names are singular
            {
                    var modelInfo = GetModelInfo<SingularTableNamesDbContext>(ShamanOptions.Default);
                    var dbSet = modelInfo.DbSet<User>();
                    Assert.NotNull(dbSet);
                    Assert.Equal("User", dbSet.TableName);
                }
            // with patching
            {
                    var modelInfo = GetModelInfo<SingularTableNamesDbContext>();
                    var dbSet = modelInfo.DbSet<User>();
                    Assert.NotNull(dbSet);
                    Assert.Equal(expectedTableName, dbSet.TableName);
                }
            });

            {
                var mi = ModelInfo.Make<SingularTableNamesDbContext>();
                var dbSet = mi.DbSets.Single(a => a.EntityType == typeof(User));
                Assert.Equal(expectedTableName, dbSet.TableName);
            }
        }

        
        [Fact]
        public void T11_ShouldCreateUnicodeColumns()
        {
            // todo: xunit tests (each test in separate appdomain). DbContext creates Model only once  
            DoTestOnModelBuilder<UnicodeTestDbContext>(true, mb =>
            {
                var modelInfo = GetModelInfo<UnicodeTestDbContext>();
                var dbSet = modelInfo.DbSet<UnicodeTestDbContext.SomeEntity>();
                Assert.NotNull(dbSet);

                var prop = dbSet.Properites.Single(a => a.PropertyName == nameof(UnicodeTestDbContext.SomeEntity.Unicode));
                Assert.True(prop.IsUnicode);
                
                prop = dbSet.Properites.Single(a => a.PropertyName == nameof(UnicodeTestDbContext.SomeEntity.NoUnicode));
                Assert.False(prop.IsUnicode);
                
                prop = dbSet.Properites.Single(a => a.PropertyName == nameof(UnicodeTestDbContext.SomeEntity.Default));
                Assert.Null(prop.IsUnicode);
            });
        }
        
        #endregion
    }
}