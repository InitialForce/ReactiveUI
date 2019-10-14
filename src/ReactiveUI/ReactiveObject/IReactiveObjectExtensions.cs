// Copyright (c) 2019 .NET Foundation and Contributors. All rights reserved.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace ReactiveUIInitialForce
{
    /// <summary>
    /// Extension methods associated with the IReactiveObject interface.
    /// </summary>
    [Preserve(AllMembers = true)]
    public static class IReactiveObjectExtensions
    {
        /// <summary>
        /// RaiseAndSetIfChanged fully implements a Setter for a read-write
        /// property on a ReactiveObject, using CallerMemberName to raise the notification
        /// and the ref to the backing field to set the property.
        /// </summary>
        /// <typeparam name="TObj">The type of the This.</typeparam>
        /// <typeparam name="TRet">The type of the return value.</typeparam>
        /// <param name="reactiveObject">The <see cref="ReactiveObject"/> raising the notification.</param>
        /// <param name="backingField">A Reference to the backing field for this
        /// property.</param>
        /// <param name="newValue">The new value.</param>
        /// <param name="propertyName">The name of the property, usually
        /// automatically provided through the CallerMemberName attribute.</param>
        /// <returns>The newly set value, normally discarded.</returns>
        public static TRet RaiseAndSetIfChanged<TObj, TRet>(
            this TObj reactiveObject,
            ref TRet backingField,
            TRet newValue,
            [CallerMemberName] string propertyName = null)
            where TObj : IReactiveObject
        {
            Contract.Requires(propertyName != null);

            if (EqualityComparer<TRet>.Default.Equals(backingField, newValue))
            {
                return newValue;
            }

            reactiveObject.RaisePropertyChanging(propertyName);
            backingField = newValue;
            reactiveObject.RaisePropertyChanged(propertyName);
            return newValue;
        }

        /// <summary>
        /// Use this method in your ReactiveObject classes when creating custom
        /// properties where raiseAndSetIfChanged doesn't suffice.
        /// </summary>
        /// <typeparam name="TSender">The sender type.</typeparam>
        /// <param name="reactiveObject">The instance of ReactiveObject on which the property has changed.</param>
        /// <param name="propertyName">
        /// A string representing the name of the property that has been changed.
        /// Leave <c>null</c> to let the runtime set to caller member name.
        /// </param>
        public static void RaisePropertyChanged<TSender>(this TSender reactiveObject, [CallerMemberName] string propertyName = null)
            where TSender : IReactiveObject
        {
            reactiveObject.RaisePropertyChanged(propertyName);
        }

        /// <summary>
        /// Use this method in your ReactiveObject classes when creating custom
        /// properties where raiseAndSetIfChanged doesn't suffice.
        /// </summary>
        /// <typeparam name="TSender">The sender type.</typeparam>
        /// <param name="reactiveObject">The instance of ReactiveObject on which the property has changed.</param>
        /// <param name="propertyName">
        /// A string representing the name of the property that has been changed.
        /// Leave <c>null</c> to let the runtime set to caller member name.
        /// </param>
        public static void RaisePropertyChanging<TSender>(this TSender reactiveObject, [CallerMemberName] string propertyName = null)
            where TSender : IReactiveObject
        {
            reactiveObject.RaisePropertyChanging(propertyName);
        }

        /// <summary>
        /// Filter a list of change notifications, returning the last change for each PropertyName in original order.
        /// </summary>
        private static IEnumerable<IReactivePropertyChangedEventArgs<TSender>> Dedup<TSender>(IList<IReactivePropertyChangedEventArgs<TSender>> batch)
        {
            if (batch.Count <= 1)
            {
                return batch;
            }

            var seen = new HashSet<string>();
            var unique = new LinkedList<IReactivePropertyChangedEventArgs<TSender>>();

            for (int i = batch.Count - 1; i >= 0; i--)
            {
                if (seen.Add(batch[i].PropertyName))
                {
                    unique.AddFirst(batch[i]);
                }
            }

            return unique;
        }
    }
}
