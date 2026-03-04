// DesktopMediaServer/Macros/MacroRegistry.cs
using System;
using System.Collections.Generic;

namespace DesktopMediaServer.Macros
{
    // Keep only ONE copy of this file in the project.
    public sealed record MacroDef(string Id, string Name, string Description);

    public sealed class MacroRegistry
    {
        private readonly Dictionary<string, (MacroDef def, Action run)> _macros = new();

        public void Add(string id, string name, string description, Action run)
            => _macros[id] = (new MacroDef(id, name, description), run);

        public IReadOnlyList<MacroDef> List()
        {
            var list = new List<MacroDef>(_macros.Count);
            foreach (var kv in _macros.Values) list.Add(kv.def);
            return list;
        }

        public bool TryRun(string id)
        {
            if (!_macros.TryGetValue(id, out var m)) return false;
            m.run();
            return true;
        }
    }
}