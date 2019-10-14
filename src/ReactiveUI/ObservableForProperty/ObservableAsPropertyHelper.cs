// Copyright (c) 2019 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.Contracts;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Splat;

namespace ReactiveUIInitialForce
{
    /// <summary>
    /// ObservableAsPropertyHelper is a class to help ViewModels implement
    /// "output properties", that is, a property that is backed by an
    /// Observable. The property will be read-only, but will still fire change
    /// notifications. This class can be created directly, but is more often created
    /// via the <see cref="OAPHCreationHelperMixin" /> extension methods.
    /// </summary>
    /// <typeparam name="T">The type.</typeparam>
    public sealed class ObservableAsPropertyHelper<T> : IHandleObservableErrors, IDisposable, IEnableLogger
    {
        private readonly Lazy<ISubject<Exception>> _thrownExceptions;
        private readonly IObservable<T> _source;
        private readonly IReactiveObject _reactiveObject;
        private readonly string _propertyName;
        private T _lastValue;
        private CompositeDisposable _disposable = new CompositeDisposable();
        private int _activated;
        private readonly Action<T> _onNext;
        private readonly Action<Exception> _onError;
        private readonly IScheduler _scheduler;

        /// <summary>
        /// Initializes a new instance of the <see cref="ObservableAsPropertyHelper{T}"/> class.
        /// </summary>
        /// <param name="observable">
        /// The Observable to base the property on.
        /// </param>
        /// <param name="reactiveObject">
        /// The reactiveObject.
        /// </param>
        /// <param name="propertyName">
        /// The name of the property to raise an event for.
        /// </param>
        /// <param name="initialValue">
        /// The initial value of the property.
        /// </param>
        /// <param name="deferSubscription">
        /// A value indicating whether the <see cref="ObservableAsPropertyHelper{T}"/>
        /// should defer the subscription to the <paramref name="observable"/> source
        /// until the first call to <see cref="Value"/>, or if it should immediately
        /// subscribe to the the <paramref name="observable"/> source.
        /// </param>
        /// <param name="scheduler">
        /// The scheduler that the notifications will provided on - this
        /// should normally be a Dispatcher-based scheduler.
        /// </param>
        public ObservableAsPropertyHelper(
            IObservable<T> observable,
            IReactiveObject reactiveObject,
            string propertyName,
            T initialValue = default(T),
            bool deferSubscription = false,
            IScheduler scheduler = null)
        {
            Contract.Requires(observable != null);
            Contract.Requires(propertyName != null);

            _scheduler = scheduler;

            _onNext = x =>
            {
                IReactiveObjectExtensions.RaisePropertyChanging(_reactiveObject, _propertyName);
                _lastValue = x;
                IReactiveObjectExtensions.RaisePropertyChanged(_reactiveObject, _propertyName);
            };
            _onError = ex => _thrownExceptions.Value.OnNext(ex);

            _thrownExceptions = new Lazy<ISubject<Exception>>(() =>
                new ScheduledSubject<Exception>(CurrentThreadScheduler.Instance, RxApp.DefaultExceptionHandler));

            _reactiveObject = reactiveObject;
            _propertyName = propertyName;
            _lastValue = initialValue;
            _source = observable.StartWith(initialValue).DistinctUntilChanged();
            if (!deferSubscription)
            {
                if(_scheduler != null)
                {
                    _source.ObserveOn(_scheduler).Subscribe(_onNext, _onError).DisposeWith(_disposable);
                }
                else
                {
                    _source.Subscribe(_onNext, _onError).DisposeWith(_disposable);
                }
                _activated = 1;
            }
        }

        private void OnError(Exception ex) => _thrownExceptions.Value.OnNext(ex);

        /// <summary>
        /// Gets the last provided value from the Observable.
        /// </summary>
        public T Value
        {
            get
            {
                if (Interlocked.CompareExchange(ref _activated, 1, 0) == 0)
                {
                    if (_scheduler != null)
                    {
                        _source.ObserveOn(_scheduler).Subscribe(_onNext, _onError).DisposeWith(_disposable);
                    }
                    else
                    {
                        _source.Subscribe(_onNext, _onError).DisposeWith(_disposable);
                    }
                }

                return _lastValue;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the ObservableAsPropertyHelper
        /// has subscribed to the source Observable.
        /// Useful for scenarios where you use deferred subscription and want to know if
        /// the ObservableAsPropertyHelper Value has been accessed yet.
        /// </summary>
        public bool IsSubscribed => _activated > 0;

        /// <summary>
        /// Gets an observable which signals whenever an exception would normally terminate ReactiveUI
        /// internal state.
        /// </summary>
        public IObservable<Exception> ThrownExceptions => _thrownExceptions.Value;

        /// <summary>
        /// Disposes this ObservableAsPropertyHelper.
        /// </summary>
        public void Dispose()
        {
            _disposable?.Dispose();
            _disposable = null;
        }
    }
}
