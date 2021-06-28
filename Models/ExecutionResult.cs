using System;
using System.Collections.Generic;

namespace blazoryn.Models
{
    public class ExecutionResult
    {
        public TimeSpan Duration { get; set; }
        public string Output { get; set; }
        public Exception Exception { get; set; }
    }
}