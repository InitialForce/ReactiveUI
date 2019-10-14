using System;
using System.Linq;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.CsProj;

namespace ReactiveUI.Benchmarks
{
 /// <summary>
    /// Benchmarks between various versions of a NuGet package
    /// </summary>
    /// <remarks>
    /// Only supported with the CsProjCoreToolchain toolchain
    /// </remarks>
    [MemoryDiagnoser]
    [ClrJob]
    [CoreJob]
    public class RaisePropertyChanged
    {
        private TestClassBaseLine _classBaseLine;
        private TestClassOptimized _classOptimized;

        [GlobalSetup]
        public void GlobalSetup()
        {
            int updates = 0;
            _classBaseLine = new TestClassBaseLine();
            _classBaseLine.PropertyChanged += (sender, args) =>
            {
                updates++;
            };
            _classOptimized = new TestClassOptimized();
            _classOptimized.PropertyChanged += (sender, args) =>
            {
                updates++;
            };
        }

        class TestClassBaseLine : ReactiveUI.ReactiveObject
        {
            private int _property;

            public int Property
            {
                get
                {
                    return _property;
                }
                set
                {
                    this.RaiseAndSetIfChanged(ref _property, value);
                }
            }
        }
        class TestClassOptimized : ReactiveUIInitialForce.ReactiveObject
        {
            private int _property;

            public int Property
            {
                get
                {
                    return _property;
                }
                set
                {
                    this.RaiseAndSetIfChanged(ref _property, value);
                }
            }
        }


        [Benchmark(Baseline = true)]
        public void BaseLine()
        {
            for (int i = 0; i < 1000; i++)
                _classBaseLine.Property += 1;
        }

        [Benchmark()]
        public void Optimized()
        {
            for (int i = 0; i < 1000; i++)
                _classOptimized.Property += 1;
        }
    }

}
