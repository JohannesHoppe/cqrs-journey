// ==============================================================================================================
// Microsoft patterns & practices
// CQRS Journey project
// ==============================================================================================================
// �2012 Microsoft. All rights reserved. Certain content used with permission from contributors
// http://go.microsoft.com/fwlink/p/?LinkID=258575
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in compliance 
// with the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software distributed under the License is 
// distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and limitations under the License.
// ==============================================================================================================

using System;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Conference.Common;
using Conference.Common.Entity;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace WorkerRoleCommandProcessor
{
    public class WorkerRole : RoleEntryPoint
    {
        private bool running;

        private bool InstrumentationEnabled {
            get {
                bool instrumentationEnabled;
                if (!bool.TryParse(RoleEnvironment.GetConfigurationSettingValue("InstrumentationEnabled"), out instrumentationEnabled)) {
                    instrumentationEnabled = false;
                }

                return instrumentationEnabled;
            }
        }

        public override void Run()
        {
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            running = true;

            while (running) {
                if (!MaintenanceMode.IsInMaintainanceMode) {
                    Trace.WriteLine("Starting the command processor", "Information");
                    using (var processor = new ConferenceProcessor(InstrumentationEnabled)) {
                        processor.Start();

                        while (running && !MaintenanceMode.IsInMaintainanceMode) {
                            Thread.Sleep(10000);
                        }

                        processor.Stop();

                        // cause the process to recycle
                        return;
                    }
                }
                Trace.TraceWarning("Starting the command processor in mantainance mode.");
                while (running && MaintenanceMode.IsInMaintainanceMode) {
                    Thread.Sleep(10000);
                }
            }

            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Trace.TraceError("Unobserved task exception: \r\n{0}", e.Exception);
        }

        public override bool OnStart()
        {
            RoleEnvironment.Changing += (sender, e) => {
                if (e.Changes
                    .OfType<RoleEnvironmentConfigurationSettingChange>()
                    .Any(x => x.ConfigurationSettingName != MaintenanceMode.MaintenanceModeSettingName)) {
                    Trace.TraceInformation("Recycling worker role because of configuration change");
                    e.Cancel = true;
                }
            };
            RoleEnvironment.Changed += (sender, e) => {
                if (e.Changes
                    .OfType<RoleEnvironmentConfigurationSettingChange>()
                    .Any(x => x.ConfigurationSettingName == MaintenanceMode.MaintenanceModeSettingName)) {
                    Trace.TraceInformation("Refreshing maintenance mode because of configuration change");
                    MaintenanceMode.RefreshIsInMaintainanceMode();
                }
            };
            MaintenanceMode.RefreshIsInMaintainanceMode();

            var config = DiagnosticMonitor.GetDefaultInitialConfiguration();

            var cloudStorageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString"));

            TimeSpan transferPeriod;
            if (!TimeSpan.TryParse(RoleEnvironment.GetConfigurationSettingValue("Diagnostics.ScheduledTransferPeriod"), out transferPeriod)) {
                transferPeriod = TimeSpan.FromMinutes(1);
            }

            TimeSpan sampleRate;
            if (!TimeSpan.TryParse(RoleEnvironment.GetConfigurationSettingValue("Diagnostics.PerformanceCounterSampleRate"), out sampleRate)) {
                sampleRate = TimeSpan.FromSeconds(30);
            }

            LogLevel logLevel;
            if (!Enum.TryParse(RoleEnvironment.GetConfigurationSettingValue("Diagnostics.LogLevelFilter"), out logLevel)) {
                logLevel = LogLevel.Verbose;
            }

            // Setup performance counters
            config.PerformanceCounters.DataSources.Add(
                new PerformanceCounterConfiguration {
                    CounterSpecifier = @"\Processor(_Total)\% Processor Time",
                    SampleRate = sampleRate
                });

#if !LOCAL
            foreach (var counterName in
                new[] 
                { 
                    Infrastructure.Azure.Instrumentation.SessionSubscriptionReceiverInstrumentation.TotalSessionsCounterName,
                    Infrastructure.Azure.Instrumentation.SessionSubscriptionReceiverInstrumentation.CurrentSessionsCounterName,
                    Infrastructure.Azure.Instrumentation.SubscriptionReceiverInstrumentation.TotalMessagesCounterName,
                    Infrastructure.Azure.Instrumentation.SubscriptionReceiverInstrumentation.TotalMessagesSuccessfullyProcessedCounterName,
                    Infrastructure.Azure.Instrumentation.SubscriptionReceiverInstrumentation.TotalMessagesUnsuccessfullyProcessedCounterName,
                    Infrastructure.Azure.Instrumentation.SubscriptionReceiverInstrumentation.TotalMessagesCompletedCounterName,
                    Infrastructure.Azure.Instrumentation.SubscriptionReceiverInstrumentation.TotalMessagesNotCompletedCounterName,                
                    Infrastructure.Azure.Instrumentation.SubscriptionReceiverInstrumentation.MessagesReceivedPerSecondCounterName,
                    Infrastructure.Azure.Instrumentation.SubscriptionReceiverInstrumentation.AverageMessageProcessingTimeCounterName,
                    Infrastructure.Azure.Instrumentation.SubscriptionReceiverInstrumentation.CurrentMessagesInProcessCounterName,
                })
            {
                config.PerformanceCounters.DataSources.Add(
                    new PerformanceCounterConfiguration
                    {
                        CounterSpecifier = @"\" + Infrastructure.Azure.Instrumentation.Constants.ReceiversPerformanceCountersCategory + @"(*)\" + counterName,
                        SampleRate = sampleRate
                    });
            }

            foreach (var counterName in
                new[] 
                { 
                    Infrastructure.Azure.Instrumentation.EventStoreBusPublisherInstrumentation.CurrentEventPublishersCounterName,
                    Infrastructure.Azure.Instrumentation.EventStoreBusPublisherInstrumentation.EventPublishingRequestsPerSecondCounterName,
                    Infrastructure.Azure.Instrumentation.EventStoreBusPublisherInstrumentation.EventsPublishedPerSecondCounterName,
                    Infrastructure.Azure.Instrumentation.EventStoreBusPublisherInstrumentation.TotalEventsPublishedCounterName,
                    Infrastructure.Azure.Instrumentation.EventStoreBusPublisherInstrumentation.TotalEventsPublishingRequestsCounterName,
                })
            {
                config.PerformanceCounters.DataSources.Add(
                    new PerformanceCounterConfiguration
                    {
                        CounterSpecifier = @"\" + Infrastructure.Azure.Instrumentation.Constants.EventPublishersPerformanceCountersCategory + @"(*)\" + counterName,
                        SampleRate = sampleRate
                    });
            }
#endif

            config.PerformanceCounters.ScheduledTransferPeriod = transferPeriod;

            // Setup logs
            config.Logs.ScheduledTransferPeriod = transferPeriod;
            config.Logs.ScheduledTransferLogLevelFilter = logLevel;

            //            DiagnosticMonitor.Start(cloudStorageAccount, config);
            DiagnosticMonitor.Start("Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString", config);

            Trace.Listeners.Add(new DiagnosticMonitorTraceListener());
            Trace.AutoFlush = true;

            Database.DefaultConnectionFactory = new ServiceConfigurationSettingConnectionFactory(Database.DefaultConnectionFactory);

            return base.OnStart();
        }

        public override void OnStop()
        {
            running = false;
            base.OnStop();
        }
    }
}