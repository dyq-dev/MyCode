using AI.Assistant.Core.Models;

namespace AI.Assistant.Core.Interfaces;

public interface IMemoryFilter
{
    MemoryFilterResult ShouldStore(string content, string role);
}
