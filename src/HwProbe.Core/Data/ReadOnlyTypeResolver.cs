using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Jellyfin.Plugin.HwProbe.Core.Data;

/// <summary>Reads read-only list and dictionary properties into a list or dictionary, which YamlDotNet doesn't do itself.</summary>
internal sealed class ReadOnlyTypeResolver : INodeTypeResolver
{
    /// <summary>Swaps a read-only collection interface for a type that implements it.</summary>
    /// <param name="nodeEvent">The node.</param>
    /// <param name="currentType">The type so far; replaced when it's a read-only collection interface.</param>
    /// <returns>Whether the type was replaced.</returns>
    public bool Resolve(NodeEvent? nodeEvent, ref Type currentType)
    {
        if (!currentType.IsGenericType)
        {
            return false;
        }

        var definition = currentType.GetGenericTypeDefinition();
        var replacement = definition == typeof(IReadOnlyList<>) ? typeof(List<>)
            : definition == typeof(IReadOnlyDictionary<,>) ? typeof(Dictionary<,>)
            : null;
        if (replacement is null)
        {
            return false;
        }

        currentType = replacement.MakeGenericType(currentType.GetGenericArguments());
        return true;
    }
}
