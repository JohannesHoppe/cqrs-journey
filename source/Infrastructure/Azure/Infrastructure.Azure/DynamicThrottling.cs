// ==============================================================================================================
// Microsoft patterns & practices
// CQRS Journey project
// ==============================================================================================================
// ©2012 Microsoft. All rights reserved. Certain content used with permission from contributors
// http://go.microsoft.com/fwlink/p/?LinkID=258575
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance 
// with the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is 
// distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and limitations under the License.
// ==============================================================================================================

using System;
using System.Threading;

namespace Infrastructure.Azure
{
    /// <summary>
    ///     Provides a way to throttle the work depending on the number of jobs it is able to complete and whether
    ///     the job is penalized for trying to parallelize too many jobs.
    /// </summary>
    public class DynamicThrottling : IDisposable
    {
        private readonly int intervalForRestoringDegreeOfParallelism;

        private readonly int maxDegreeOfParallelism;

        private readonly int minDegreeOfParallelism;

        private readonly Timer parallelismRestoringTimer;

        private readonly int penaltyAmount;

        private readonly AutoResetEvent waitHandle = new AutoResetEvent(true);

        private readonly int workCompletedParallelismGain;

        private readonly int workFailedPenaltyAmount;

        private int currentParallelJobs;

        public int AvailableDegreesOfParallelism { get; private set; }

        /// <summary>
        ///     Initializes a new instance of <see cref="DynamicThrottling" />.
        /// </summary>
        /// <param name="maxDegreeOfParallelism">Maximum number of parallel jobs.</param>
        /// <param name="minDegreeOfParallelism">Minimum number of parallel jobs.</param>
        /// <param name="penaltyAmount">Number of degrees of parallelism to remove when penalizing slightly.</param>
        /// <param name="workFailedPenaltyAmount">Number of degrees of parallelism to remove when work fails.</param>
        /// <param name="workCompletedParallelismGain">Number of degrees of parallelism to restore on work completed.</param>
        /// <param name="intervalForRestoringDegreeOfParallelism">Interval in milliseconds to restore 1 degree of parallelism.</param>
        public DynamicThrottling(int maxDegreeOfParallelism, int minDegreeOfParallelism, int penaltyAmount, int workFailedPenaltyAmount, int workCompletedParallelismGain,
            int intervalForRestoringDegreeOfParallelism)
        {
            this.maxDegreeOfParallelism = maxDegreeOfParallelism;
            this.minDegreeOfParallelism = minDegreeOfParallelism;
            this.penaltyAmount = penaltyAmount;
            this.workFailedPenaltyAmount = workFailedPenaltyAmount;
            this.workCompletedParallelismGain = workCompletedParallelismGain;
            this.intervalForRestoringDegreeOfParallelism = intervalForRestoringDegreeOfParallelism;
            parallelismRestoringTimer = new Timer(s => IncrementDegreesOfParallelism(1));

            AvailableDegreesOfParallelism = minDegreeOfParallelism;
        }

        public void WaitUntilAllowedParallelism(CancellationToken cancellationToken)
        {
            while (currentParallelJobs >= AvailableDegreesOfParallelism) {
                if (cancellationToken.IsCancellationRequested) {
                    return;
                }

                // Trace.WriteLine("Waiting for available degrees of parallelism. Available: " + this.availableDegreesOfParallelism + ". In use: " + this.currentParallelJobs);

                waitHandle.WaitOne();
            }
        }

        public void NotifyWorkCompleted()
        {
            Interlocked.Decrement(ref currentParallelJobs);
            // Trace.WriteLine("Job finished. Parallel jobs are now: " + this.currentParallelJobs);
            IncrementDegreesOfParallelism(workCompletedParallelismGain);
        }

        public void NotifyWorkStarted()
        {
            Interlocked.Increment(ref currentParallelJobs);
            // Trace.WriteLine("Job started. Parallel jobs are now: " + this.currentParallelJobs);
        }

        public void Penalize()
        {
            // Slightly penalize with removal of some degrees of parallelism.
            DecrementDegreesOfParallelism(penaltyAmount);
        }

        public void NotifyWorkCompletedWithError()
        {
            // Largely penalize with removal of several degrees of parallelism.
            DecrementDegreesOfParallelism(workFailedPenaltyAmount);
            Interlocked.Decrement(ref currentParallelJobs);
            // Trace.WriteLine("Job finished with error. Parallel jobs are now: " + this.currentParallelJobs);
            waitHandle.Set();
        }

        public void Start(CancellationToken cancellationToken)
        {
            if (cancellationToken.CanBeCanceled) {
                cancellationToken.Register(() => parallelismRestoringTimer.Change(Timeout.Infinite, Timeout.Infinite));
            }

            parallelismRestoringTimer.Change(intervalForRestoringDegreeOfParallelism, intervalForRestoringDegreeOfParallelism);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) {
                waitHandle.Dispose();
                parallelismRestoringTimer.Dispose();
            }
        }

        private void IncrementDegreesOfParallelism(int count)
        {
            if (AvailableDegreesOfParallelism < maxDegreeOfParallelism) {
                AvailableDegreesOfParallelism += count;
                if (AvailableDegreesOfParallelism >= maxDegreeOfParallelism) {
                    AvailableDegreesOfParallelism = maxDegreeOfParallelism;
                    // Trace.WriteLine("Incremented available degrees of parallelism. Available: " + this.availableDegreesOfParallelism);
                }
            }

            waitHandle.Set();
        }

        private void DecrementDegreesOfParallelism(int count)
        {
            AvailableDegreesOfParallelism -= count;
            if (AvailableDegreesOfParallelism < minDegreeOfParallelism) {
                AvailableDegreesOfParallelism = minDegreeOfParallelism;
            }
            // Trace.WriteLine("Decremented available degrees of parallelism. Available: " + this.availableDegreesOfParallelism);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}