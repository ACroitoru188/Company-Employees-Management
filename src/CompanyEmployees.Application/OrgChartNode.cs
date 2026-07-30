using System;
using System.Collections.Generic;

namespace CompanyEmployees.Application
{
    public class OrgChartNode
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;

        // Visual properties
        public bool IsFocusNode { get; set; } // If true, this node or its branch is the user's focus

        public List<OrgChartNode> Subordinates { get; set; } = new();
    }
}
