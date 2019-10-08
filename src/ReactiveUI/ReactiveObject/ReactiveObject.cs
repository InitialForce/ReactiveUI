// Copyright (c) 2019 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using DynamicData.Annotations;
using Splat;

namespace ReactiveUI
{
    /// <summary>
    /// ReactiveObject is the base object for ViewModel classes, and it
    /// implements INotifyPropertyChanged. In addition, ReactiveObject provides
    /// Changing and Changed Observables to monitor object changes.
    /// </summary>
    [DataContract]
    public class ReactiveObject : IReactiveNotifyPropertyChanged<IReactiveObject>, IHandleObservableErrors, IReactiveObject
    {
        private readonly Lazy<(ISubject<IReactivePropertyChangedEventArgs<IReactiveObject>> subject,
            IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> observable)> _changed;

        [NotNull]
        private readonly ConcurrentDictionary<string, ReactivePropertyChangedEventArgs<ReactiveObject>>
            _changedDict =
                new ConcurrentDictionary<string, ReactivePropertyChangedEventArgs<ReactiveObject>>();

        private readonly Lazy<(ISubject<IReactivePropertyChangedEventArgs<IReactiveObject>> subject,
            IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> observable)> _changing;

        [NotNull]
        private readonly ConcurrentDictionary<string, ReactivePropertyChangingEventArgs<ReactiveObject>>
            _changingDict =
                new ConcurrentDictionary<string, ReactivePropertyChangingEventArgs<ReactiveObject>>();

        private readonly Lazy<Subject<Unit>> _startDelayNotifications = new Lazy<Subject<Unit>>();

        private readonly Lazy<ISubject<Exception>> _thrownExceptions = new Lazy<ISubject<Exception>>(() =>
            new ScheduledSubject<Exception>(Scheduler.Immediate, RxApp.DefaultExceptionHandler));

        private readonly Func<string, ReactivePropertyChangedEventArgs<ReactiveObject>> _valueFactoryChanged;

        private readonly Func<string, ReactivePropertyChangingEventArgs<ReactiveObject>> _valueFactoryChanging;

        private long _changeNotificationsDelayed;

        private long _changeNotificationsSuppressed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReactiveObject" /> class.
        /// </summary>
        public ReactiveObject()
        {
            _valueFactoryChanged = v => new ReactivePropertyChangedEventArgs<ReactiveObject>(this, v);
            _valueFactoryChanging = v => new ReactivePropertyChangingEventArgs<ReactiveObject>(this, v);
            _changing =
                new Lazy<(ISubject<IReactivePropertyChangedEventArgs<IReactiveObject>>,
                    IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>>)>(() =>
                {
                    var changingSubject = new Subject<IReactivePropertyChangedEventArgs<IReactiveObject>>();
                    IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> changedObs =
                        changingSubject.Buffer(changingSubject.Where(_ => !AreChangeNotificationsDelayed())
                                .Select(_ => Unit.Default)
                                .Merge(_startDelayNotifications.Value))
                            .SelectMany(Dedup)
                            .Publish()
                            .RefCount();

                    return (changingSubject, changedObs);
                });

            _changed =
                new Lazy<(ISubject<IReactivePropertyChangedEventArgs<IReactiveObject>> subject,
                    IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> observable)>(() =>
                {
                    var changedSubject = new Subject<IReactivePropertyChangedEventArgs<IReactiveObject>>();
                    IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> changedObs = changedSubject
                        .Buffer(changedSubject.Where(_ => !AreChangeNotificationsDelayed())
                            .Select(_ => Unit.Default)
                            .Merge(_startDelayNotifications.Value))
                        .SelectMany(Dedup)
                        .Publish()
                        .RefCount();

                    return (changedSubject, changedObs);
                });
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler PropertyChanged;

        /// <inheritdoc />
        public event PropertyChangingEventHandler PropertyChanging;

        /// <inheritdoc />
        [IgnoreDataMember]
        public IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> Changed => _changed.Value.observable;

        /// <inheritdoc />
        [IgnoreDataMember]
        public IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> Changing => _changing.Value.observable;

        /// <inheritdoc />
        [IgnoreDataMember]
        public IObservable<Exception> ThrownExceptions => _thrownExceptions.Value;

        /// <summary>
        /// Check if change notifications are delayed.
        /// </summary>
        /// <returns>Delayed or not.</returns>
        public bool AreChangeNotificationsDelayed()
        {
            return Interlocked.Read(ref _changeNotificationsDelayed) > 0;
        }

        /// <summary>
        /// Check if change notifications are enabled.
        /// </summary>
        /// <returns>Enabled or not.</returns>
        public bool AreChangeNotificationsEnabled()
        {
            return Interlocked.Read(ref _changeNotificationsSuppressed) == 0;
        }

        /// <summary>
        ///     When this method is called, an object will not dispatch change
        ///     Observable notifications until the return value is disposed.
        ///     When the Disposable it will dispatched all queued notifications.
        ///     If this method is called multiple times it will reference count
        ///     and not perform notification until all values returned are disposed.
        /// </summary>
        /// <returns>
        ///     An object that, when disposed, re-enables Observable change
        ///     notifications.
        /// </returns>
        public IDisposable DelayChangeNotifications()
        {
            if (Interlocked.Increment(ref _changeNotificationsDelayed) == 1)
            {
                if (_startDelayNotifications.IsValueCreated)
                {
                    _startDelayNotifications.Value.OnNext(Unit.Default);
                }
            }

            return Disposable.Create(() =>
            {
                if (Interlocked.Decrement(ref _changeNotificationsDelayed) == 0)
                {
                    if (_startDelayNotifications.IsValueCreated)
                    {
                        _startDelayNotifications.Value.OnNext(Unit.Default);
                    }
                }
            });
        }

        /// <summary>
        /// RaiseAndSetIfChanged fully implements a Setter for a read-write
        /// property on a ReactiveObject, using CallerMemberName to raise the notification.
        /// </summary>
        /// <typeparam name="TRet">The property type.</typeparam>
        /// <param name="backingField">Ref to field.</param>
        /// <param name="newValue">The new value.</param>
        /// <param name="propertyName">Optional property name.</param>
        /// <returns>The new value of the field.</returns>
        public TRet RaiseAndSetIfChanged<TRet>(
            ref TRet backingField,
            TRet newValue,
            [CallerMemberName] string propertyName = null)
        {
            Contract.Requires(propertyName != null);

            if (EqualityComparer<TRet>.Default.Equals(backingField, newValue))
            {
                return newValue;
            }

            RaisePropertyChanging(propertyName);
            backingField = newValue;
            RaisePropertyChanged(propertyName);
            return newValue;
        }

        /// <inheritdoc />
        public void RaisePropertyChanged(PropertyChangedEventArgs args)
        {
            PropertyChanged?.Invoke(this, args);
        }

        /// <inheritdoc />
        public void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            Debug.Assert(propertyName != null, nameof(propertyName) + " != null");

            if (!AreChangeNotificationsEnabled())
            {
                return;
            }

            ReactivePropertyChangedEventArgs<ReactiveObject> changed = null;
            PropertyChangedEventHandler onPropertyChanged = PropertyChanged;

            if (onPropertyChanged != null || _changed.IsValueCreated)
            {
                changed = _changedDict.GetOrAdd(propertyName, _valueFactoryChanged);
            }

            onPropertyChanged?.Invoke(this, changed);

            if (_changed.IsValueCreated)
            {
                NotifyObservable(this, changed, _changed.Value.subject);
            }
        }

        /// <inheritdoc />
        public IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> GetChangingObservable()
        {
            return Changing;
        }

        /// <inheritdoc />
        public IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> GetChangedObservable()
        {
            return Changed;
        }

        /// <inheritdoc />
        public void RaisePropertyChanging(PropertyChangingEventArgs args)
        {
            PropertyChanging?.Invoke(this, args);
        }

        /// <inheritdoc />
        public void RaisePropertyChanging(string propertyName)
        {
            if (!AreChangeNotificationsEnabled())
            {
                return;
            }

            ReactivePropertyChangingEventArgs<ReactiveObject> changing = null;

            PropertyChangingEventHandler onPropertyChanging = PropertyChanging;
            if (onPropertyChanging != null || _changing.IsValueCreated)
            {
                changing = _changingDict.GetOrAdd(propertyName, _valueFactoryChanging);
            }

            onPropertyChanging?.Invoke(this, changing);

            if (_changing.IsValueCreated)
            {
                NotifyObservable(this, changing, _changing?.Value.subject);
            }
        }

        /// <inheritdoc />
        public IDisposable SuppressChangeNotifications()
        {
            Interlocked.Increment(ref _changeNotificationsSuppressed);
            return Disposable.Create(() => Interlocked.Decrement(ref _changeNotificationsSuppressed));
        }

        /// <summary>
        ///     Filter a list of change notifications, returning the last change for each PropertyName in original order.
        /// </summary>
        private static IEnumerable<IReactivePropertyChangedEventArgs<TSender>> Dedup<TSender>(
            IList<IReactivePropertyChangedEventArgs<TSender>> batch)
        {
            if (batch.Count <= 1)
            {
                return batch;
            }

            var seen = new HashSet<string>();
            var unique = new LinkedList<IReactivePropertyChangedEventArgs<TSender>>();

            for (int i = batch.Count - 1; i >= 0; i--)
            {
                if (seen.Add(batch[i]
                    .PropertyName))
                {
                    unique.AddFirst(batch[i]);
                }
            }

            return unique;
        }

        private void NotifyObservable(
            ReactiveObject rxObj,
            IReactivePropertyChangedEventArgs<IReactiveObject> item,
            ISubject<IReactivePropertyChangedEventArgs<IReactiveObject>> subject)
        {
            try
            {
                subject.OnNext(item);
            }
            catch (Exception ex)
            {
                rxObj.Log()
                    .Error(ex, "ReactiveObject Subscriber threw exception");
                if (_thrownExceptions.IsValueCreated)
                {
                    _thrownExceptions.Value.OnNext(ex);
                    return;
                }

                throw;
            }
        }
    }
}

// vim: tw=120 ts=4 sw=4 et :
