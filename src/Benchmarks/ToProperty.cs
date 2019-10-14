using System;
using System.Linq;
using System.Reactive.Subjects;
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
//    [CoreJob]
    public class ToProperty
    {
        private TestClassBaseLine _classBaseLine;
        private TestClassOptimized _classOptimized;
        private Subject<int> _observableBaseLine;
        private Subject<int> _observableOptimized;
        private int _val;

        [GlobalSetup]
        public void GlobalSetup()
        {
            int updates = 0;
            _observableBaseLine = new Subject<int>();
            _classBaseLine = new TestClassBaseLine(_observableBaseLine);
            _classBaseLine.PropertyChanged += (sender, args) =>
            {
                updates++;
            };
            _observableOptimized = new Subject<int>();
            _classOptimized = new TestClassOptimized(_observableOptimized);
            _classOptimized.PropertyChanged += (sender, args) =>
            {
                updates++;
            };
        }

        class TestClassBaseLine : ReactiveUI.ReactiveObject
        {
            public TestClassBaseLine(IObservable<int> obs)
            {
                _property = obs.ToProperty(this, vm => vm.Property);
            }

            private readonly ObservableAsPropertyHelper<int> _property;

            public int Property
            {
                get
                {
                    return _property.Value;
                }
            }
        }

        class TestClassOptimized : ReactiveUIInitialForce.ReactiveObject
        {
            private readonly ReactiveUIInitialForce.ObservableAsPropertyHelper<int> _property;

            public TestClassOptimized(IObservable<int> obs)
            {
                _property = ReactiveUIInitialForce.OAPHCreationHelperMixin.ToProperty(obs, this, vm => vm.Property);
            }

            public int Property
            {
                get
                {
                    return _property.Value;
                }
            }
        }

        [Benchmark(Baseline = true)]
        public void BaseLine()
        {
            for (int i = 0; i < 1000; i++)
                _observableBaseLine.OnNext(_val++);
        }

        [Benchmark()]
        public void Optimized()
        {
            for (int i = 0; i < 1000; i++)
                _observableOptimized.OnNext(_val++);
        }
    }
}
