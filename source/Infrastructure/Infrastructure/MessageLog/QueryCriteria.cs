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
using System.Collections.Generic;

namespace Infrastructure.MessageLog
{
    /// <summary>
    ///     The query criteria for filtering events from the message log when reading.
    /// </summary>
    public class QueryCriteria
    {
        public ICollection<string> SourceTypes { get; }

        public ICollection<string> SourceIds { get; }

        public ICollection<string> AssemblyNames { get; }

        public ICollection<string> Namespaces { get; }

        public ICollection<string> FullNames { get; }

        public ICollection<string> TypeNames { get; }

        public DateTime? EndDate { get; set; }

        public QueryCriteria()
        {
            SourceTypes = new List<string>();
            SourceIds = new List<string>();
            AssemblyNames = new List<string>();
            Namespaces = new List<string>();
            FullNames = new List<string>();
            TypeNames = new List<string>();
        }
    }
}