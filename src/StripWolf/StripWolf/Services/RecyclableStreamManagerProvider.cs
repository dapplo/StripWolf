using Microsoft.IO;

namespace StripWolf.Services;

public static class RecyclableStreamManagerProvider
{
    public static RecyclableMemoryStreamManager Manager { get; } = new();
}
