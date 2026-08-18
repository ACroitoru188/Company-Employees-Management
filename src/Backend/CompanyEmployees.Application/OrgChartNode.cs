using System;
using System.Collections.Generic;
using CompanyEmployees.Domain.Enums;

namespace CompanyEmployees.Application
{
    public class OrgChartNode
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Site { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public Guid? ManagerId { get; set; }

        // Request information
        public bool HasPendingRequest { get; set; }
        public Guid? PendingRequestId { get; set; }
        public string? PendingRequestType { get; set; }
        public string? PendingRequestDates { get; set; }

        // Contract information
        public bool HasContract { get; set; }
        public Guid? ContractId { get; set; }
        public ContractType? ContractType { get; set; }
        public ContractStatus? ContractStatus { get; set; }
        public DateOnly? ContractStartDate { get; set; }
        public DateOnly? ContractEndDate { get; set; }

        // Visual properties
        public bool IsFocusNode { get; set; } // If true, this node or its branch is the user's focus
        public bool IsExpanded { get; set; } = true; // If false, subordinates are hidden
        public bool IsLoading { get; set; } // If true, data is currently being fetched
        public bool HasUnloadedChildren { get; set; } // If true, has children but they are not loaded
        public bool HasChildren => Subordinates.Count > 0 || HasUnloadedChildren;

        // Mathematical Layout Properties
        public double X { get; set; }
        public double Y { get; set; }
        public double SubtreeWidth { get; set; }
        public int Depth { get; set; }

        public List<OrgChartNode> Subordinates { get; set; } = new List<OrgChartNode>();
    }
}

