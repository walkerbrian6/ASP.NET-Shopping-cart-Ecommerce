﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Dasync.Collections;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Smartstore.Domain;

namespace Smartstore.Data
{
    [Flags]
    public enum DataProviderFeatures
    {
        None = 0,
        Backup = 1 << 0,
        Restore = 1 << 1,
        Shrink = 1 << 2,
        ReIndex = 1 << 3,
        ComputeSize = 1 << 4,
        AccessIncrement = 1 << 5,
        StreamBlob = 1 << 6,
        ExecuteSqlScript = 1 << 7,
        StoredProcedures = 1 << 8
    }

    public abstract class DataProvider : Disposable
    {
        protected DataProvider(DatabaseFacade database)
        {
            // TODO: (core) Add more methods: EnsureColumn(), ... 
            Database = database;
        }

        public DatabaseFacade Database { get; }

        protected DbContext Context
        {
            get => ((IDatabaseFacadeDependenciesAccessor)Database).Context;
        }

        #region Feature flags

        public virtual DataProviderFeatures Features { get; } = DataProviderFeatures.None;

        public bool CanBackup
        {
            get => Features.HasFlag(DataProviderFeatures.Backup);
        }

        public bool CanRestore
        {
            get => Features.HasFlag(DataProviderFeatures.Restore);
        }

        public bool CanShrink
        {
            get => Features.HasFlag(DataProviderFeatures.Shrink);
        }

        public bool CanReIndex
        {
            get => Features.HasFlag(DataProviderFeatures.ReIndex);
        }

        public bool CanComputeSize
        {
            get => Features.HasFlag(DataProviderFeatures.ComputeSize);
        }

        public bool CanAccessIncrement
        {
            get => Features.HasFlag(DataProviderFeatures.AccessIncrement);
        }

        public bool CanStreamBlob
        {
            get => Features.HasFlag(DataProviderFeatures.StreamBlob);
        }

        public bool CanExecuteSqlScript
        {
            get => Features.HasFlag(DataProviderFeatures.ExecuteSqlScript);
        }

        public bool CanExecuteStoredProcedures
        {
            get => Features.HasFlag(DataProviderFeatures.StoredProcedures);
        }

        #endregion

        #region Database schema

        public virtual bool HasDatabase(string databaseName)
        {
            Guard.NotEmpty(databaseName, nameof(databaseName));

            return Database.ExecuteQueryInterpolated<string>(
                $"SELECT database_id FROM sys.databases WHERE Name = {databaseName}").Any();
        }

        public virtual Task<bool> HasDatabaseAsync(string databaseName)
        {
            Guard.NotEmpty(databaseName, nameof(databaseName));

            return Database.ExecuteQueryInterpolatedAsync<string>(
                $"SELECT database_id FROM sys.databases WHERE Name = {databaseName}").AnyAsync(x => true);
        }

        public virtual bool HasTable(string tableName)
        {
            Guard.NotEmpty(tableName, nameof(tableName));

            return Database.ExecuteQueryInterpolated<string>(
                $"SELECT table_name From INFORMATION_SCHEMA.TABLES WHERE table_name = {tableName}").Any();
        }

        public virtual Task<bool> HasTableAsync(string tableName)
        {
            Guard.NotEmpty(tableName, nameof(tableName));

            return Database.ExecuteQueryInterpolatedAsync<string>($"SELECT table_name From INFORMATION_SCHEMA.TABLES WHERE table_name = {tableName}").AnyAsync(x => true);
        }

        public virtual bool HasColumn(string tableName, string columnName)
        {
            Guard.NotEmpty(tableName, nameof(tableName));
            Guard.NotEmpty(columnName, nameof(columnName));

            return Database.ExecuteQueryInterpolated<string>(
                $"SELECT column_name From INFORMATION_SCHEMA.COLUMNS WHERE table_name = {tableName} And column_name = {columnName}").Any();
        }

        public virtual Task<bool> HasColumnAsync(string tableName, string columnName)
        {
            Guard.NotEmpty(tableName, nameof(tableName));
            Guard.NotEmpty(columnName, nameof(columnName));

            return Database.ExecuteQueryInterpolatedAsync<string>(
                $"SELECT column_name From INFORMATION_SCHEMA.COLUMNS WHERE table_name = {tableName} And column_name = {columnName}").AnyAsync(x => true);
        }

        #endregion

        #region Sql

        /// <summary>
        /// Executes the given INSERT INTO sql command and returns ident of the inserted row.
        /// </summary>
        /// <returns>The ident / primary key value of the newly inserted row.</returns>
        public virtual Task<int> InsertIntoAsync(string sql, params object[] parameters)
            => throw new NotSupportedException();

        #endregion

        #region Maintenance

        /// <summary>
        /// Shrinks / compacts the database
        /// </summary>
        public virtual int ShrinkDatabase()
            => throw new NotSupportedException();

        /// <summary>
        /// Shrinks / compacts the database
        /// </summary>
        public virtual Task<int> ShrinkDatabaseAsync(CancellationToken cancelToken = default)
            => throw new NotSupportedException();

        /// <summary>
        /// Gets the total size of the database in MB.
        /// </summary>
        public virtual decimal GetDatabaseSize()
            => throw new NotSupportedException();

        /// <summary>
        /// Gets the total size of the database in MB.
        /// </summary>
        public virtual Task<decimal> GetDatabaseSizeAsync()
            => throw new NotSupportedException();

        /// <summary>
        /// Creates a database backup
        /// </summary>
        /// <param name="fullPath">The full physical path to the backup file.</param>
        public virtual int BackupDatabase(string fullPath)
            => throw new NotSupportedException();

        /// <summary>
        /// Creates a database backup
        /// </summary>
        /// <param name="fullPath">The full physical path to the backup file.</param>
        public virtual Task<int> BackupDatabaseAsync(string fullPath, CancellationToken cancelToken = default)
            => throw new NotSupportedException();

        /// <summary>
        /// Restores a database backup
        /// </summary>
        /// <param name="backupFullPath">The full physical path to the backup file to restore.</param>
        public virtual int RestoreDatabase(string backupFullPath)
            => throw new NotSupportedException();

        /// <summary>
        /// Restores a database backup
        /// </summary>
        /// <param name="backupFullPath">The full physical path to the backup file to restore.</param>
        public virtual Task<int> RestoreDatabaseAsync(string backupFullPath, CancellationToken cancelToken = default)
            => throw new NotSupportedException();

        /// <summary>
        /// Reindexes all tables
        /// </summary>
        public virtual int ReIndexTables()
            => throw new NotSupportedException();

        /// <summary>
        /// Reindexes all tables
        /// </summary>
        public virtual Task<int> ReIndexTablesAsync(CancellationToken cancelToken = default)
            => throw new NotSupportedException();

        /// <summary>
        /// Executes a (multiline) sql script
        /// </summary>
        public virtual void ExecuteSqlScript(string sqlScript)
        {
            var sqlCommands = TokenizeSqlScript(sqlScript);

            using var tx = Database.BeginTransaction();
            try
            {
                foreach (var command in sqlCommands)
                {
                    Database.ExecuteSqlRaw(command);
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
            }
        }

        /// <summary>
        /// Executes a (multiline) sql script
        /// </summary>
        public virtual async Task ExecuteSqlScriptAsync(string sqlScript, CancellationToken cancelToken = default)
        {
            var sqlCommands = TokenizeSqlScript(sqlScript);

            using var tx = await Database.BeginTransactionAsync(cancelToken);
            try
            {
                foreach (var command in sqlCommands)
                {
                    await Database.ExecuteSqlRawAsync(command, cancelToken);
                }

                await tx.CommitAsync(cancelToken);
            }
            catch
            {
                await tx.RollbackAsync(cancelToken);
            }
        }

        protected virtual IList<string> TokenizeSqlScript(string sqlScript)
            => throw new NotSupportedException();

        /// <summary>
        /// Truncates/clears a table. ALL rows will be irreversibly deleted!!!!
        /// </summary>
        public int TruncateTable<T>() where T : BaseEntity
        {
            var tableName = Context.Model.FindEntityType(typeof(T)).GetTableName();
            return Database.ExecuteSqlRaw($"TRUNCATE TABLE [{tableName}]");
        }

        /// <summary>
        /// Truncates/clears a table. ALL rows will be irreversibly deleted!!!!
        /// </summary>
        public Task<int> TruncateTableAsync<T>() where T : BaseEntity
        {
            var tableName = Context.Model.FindEntityType(typeof(T)).GetTableName();
            return Database.ExecuteSqlRawAsync($"TRUNCATE TABLE [{tableName}]");
        }

        /// <summary>
        /// Gets the current ident value
        /// </summary>
        /// <typeparam name="T">Entity</typeparam>
        /// <returns>Ident value or <c>null</c> if value cannot be resolved.</returns>
        public int? GetTableIdent<T>() where T : BaseEntity
        {
            return GetTableIncrementCore(Context.Model.FindEntityType(typeof(T)).GetTableName());
        }

        /// <summary>
        /// Gets the current ident value
        /// </summary>
        /// <typeparam name="T">Entity</typeparam>
        /// <returns>Ident value or <c>null</c> if value cannot be resolved.</returns>
        public Task<int?> GetTableIdentAsync<T>() where T : BaseEntity
        {
            return GetTableIncrementCoreAsync(Context.Model.FindEntityType(typeof(T)).GetTableName());
        }

        /// <summary>
        /// Sets the table ident value
        /// </summary>
        /// <typeparam name="T">Entity</typeparam>
        /// <param name="ident">The new ident value</param>
        public void SetTableIdent<T>(int ident) where T : BaseEntity
        {
            SetTableIncrementCore(Context.Model.FindEntityType(typeof(T)).GetTableName(), ident);
        }

        /// <summary>
        /// Sets the table auto increment value
        /// </summary>
        /// <typeparam name="T">Entity</typeparam>
        /// <param name="ident">The new ident value</param>
        public Task SetTableIncrementAsync<T>(int ident) where T : BaseEntity
        {
            return SetTableIncrementCoreAsync(Context.Model.FindEntityType(typeof(T)).GetTableName(), ident);
        }

        protected virtual int? GetTableIncrementCore(string tableName)
            => throw new NotSupportedException();

        protected virtual Task<int?> GetTableIncrementCoreAsync(string tableName)
            => throw new NotSupportedException();

        protected virtual void SetTableIncrementCore(string tableName, int ident)
            => throw new NotSupportedException();

        protected virtual Task SetTableIncrementCoreAsync(string tableName, int ident)
            => throw new NotSupportedException();

        #endregion

        #region Blob stream

        public Stream OpenBlobStream<T, TProp>(Expression<Func<T, TProp>> propertyAccessor, int id)
            where T : BaseEntity
        {
            Guard.NotNull(propertyAccessor, nameof(propertyAccessor));
            Guard.IsPositive(id, nameof(id));

            var model = Context.Model;

            var entityType = model.FindEntityType(typeof(T));
            if (entityType == null)
            {
                throw new ArgumentException($"The entity type '{typeof(T)}' is not associated with the current database context.", "T");
            }

            var propName = propertyAccessor.ExtractMemberInfo().Name;
            var entityProperty = entityType.GetProperty(propName);
            if (entityProperty == null)
            {
                throw new ArgumentException($"The property '{propName}' is not mapped to the database.", nameof(propertyAccessor));
            }

            var storeIdent = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table).Value;

            return OpenBlobStream(
                entityType.GetTableName(), 
                entityProperty.GetColumnName(storeIdent),
                nameof(BaseEntity.Id), 
                id);
        }

        public virtual Stream OpenBlobStream(string tableName, string blobColumnName, string pkColumnName, object pkColumnValue)
            => throw new NotSupportedException();

        #endregion

        #region Connection

        public abstract DataProviderType ProviderType { get; }

        public DbParameter CreateParameter(string name, object value)
        {
            Guard.NotEmpty(name, nameof(name));
            
            var p = CreateParameter();
            p.ParameterName = name;
            p.Value = value;

            return p;
        }

        public virtual DbParameter CreateParameter()
        {
            return new SqlParameter();
        }

        #endregion
    }
}
