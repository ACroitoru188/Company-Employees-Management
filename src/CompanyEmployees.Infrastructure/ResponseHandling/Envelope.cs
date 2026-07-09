using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Infrastructure.ResponseHandling
{
    public class Envelope<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Message { get; set; }
        public int StatusCode { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
