using System;
using System.IO;

namespace TiaFds.Core
{
    public sealed class SnapshotFileExistsException : IOException
    {
        public SnapshotFileExistsException(string path)
            : base("Snapshot destination already exists: " + path)
        {
        }
    }

    public sealed class SnapshotSerializationException : Exception
    {
        public SnapshotSerializationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public sealed class SnapshotValidationException : Exception
    {
        public SnapshotValidationException(string message)
            : base(message)
        {
        }
    }
}
