using System;
using BenchmarkDotNet.Attributes;

namespace LiteDbX.Benchmarks.Benchmarks
{
    public abstract class BenchmarkBase
    {
        [Params(ConnectionType.Direct)]
        public ConnectionType ConnectionType;

        // Insertion data size
        [Params(10, 50, 100, 500, 1000, 5000, 10000)]
        public int DatasetSize;

        [Params(null, "SecurePassword")]
        public string Password;

        public virtual string DatabasePath
        {
            get => Constants.DATABASE_NAME;
            set => throw new NotImplementedException();
        }

        protected ILiteDatabase DatabaseInstance { get; set; }

        public ConnectionString ConnectionString()
        {
            return new ConnectionString(DatabasePath)
            {
                Connection = ConnectionType,
                Password = Password
            };
        }
    }
}