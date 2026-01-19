using System;
using System.Collections.Generic;
using System.IO;

namespace d4rkpl4y3r.AvatarOptimizer.Util
{
    class Logger
    {
        public readonly string filePath;
        private readonly List<string> buffer = new();
        public int indentLevel = 0;
        private const int FlushThreshold = 100;

        private class Section : IDisposable
        {
            private readonly Logger logger;
            private readonly int level;
            public Section(Logger logger, int level)
            {
                this.logger = logger;
                this.level = level;
                logger.indentLevel += level;
            }
            public void Dispose() => logger.indentLevel -= level;
        }

        public Logger(string filePath)
        {
            this.filePath = filePath;
            File.WriteAllText(filePath, "");
        }

        public IDisposable IndentScope(int level = 1) => new Section(this, level);
        
        public void Append(string message)
        {
            string indent = new(' ', indentLevel * 2);
            buffer.Add($"{indent}{message}");
            if (buffer.Count >= FlushThreshold)
            {
                Flush();
            }
        }

        public void Flush()
        {
            if (buffer.Count == 0)
                return;
            File.AppendAllLines(filePath, buffer);
            buffer.Clear();
        }
    }
}