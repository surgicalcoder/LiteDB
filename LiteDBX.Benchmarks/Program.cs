using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace LiteDbX.Benchmarks
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance
                                                                                              //.With(new BenchmarkDotNet.Filters.AnyCategoriesFilter(new[] { Benchmarks.Constants.Categories.GENERAL }))
                                                                                              //.AddFilter(new BenchmarkDotNet.Filters.AnyCategoriesFilter([Benchmarks.Constants.Categories.GENERAL]))
                                                                                              .AddJob(Job.Default.WithRuntime(CoreRuntime.Core10_0)
                                                                                                                 .WithJit(Jit.RyuJit)
                                                                                                                 .WithGcForce(true))
                                                                                              /*.With(Job.Default.With(MonoRuntime.Default)
                                                                                                  .With(Jit.Llvm)
                                                                                                  .With(new[] {new MonoArgument("--optimize=inline")})
                                                                                                  .WithGcForce(true))*/
                                                                                              .AddDiagnoser(MemoryDiagnoser.Default)
                                                                                              .KeepBenchmarkFiles());
        }
    }
}