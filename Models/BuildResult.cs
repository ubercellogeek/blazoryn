using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace blazoryn.Models
{
    public class BuildResult
    {
        public TimeSpan Duration { get; set; }
        public byte[] ILBytes { get; set; }
        public ImmutableArray<Diagnostic> Logs { get; set; }
        public bool Success { get; set; }
        public Exception Exception { get; set; }
    }
}