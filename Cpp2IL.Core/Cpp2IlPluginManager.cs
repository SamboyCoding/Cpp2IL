using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;
using Cpp2IL.Core.Logging;

namespace Cpp2IL.Core;

public static class Cpp2IlPluginManager
{
    private static List<Cpp2IlPlugin> _loadedPlugins = [];

    [RequiresUnreferencedCode("Plugins are loaded dynamically.")]
    internal static void LoadFromDirectory(string pluginsDir)
    {
        Logger.InfoNewline($"Loading plugins from {pluginsDir}...", "Plugins");

        if (!Directory.Exists(pluginsDir))
            return;

        foreach (var file in Directory.EnumerateFiles(pluginsDir))
        {
            if (Path.GetExtension(file) == ".dll")
            {
                Logger.VerboseNewline($"\tLoading {Path.GetFileName(file)}...", "Plugins");
                Assembly.LoadFrom(file);
            }
        }
    }

    internal static void InitAll()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        Logger.VerboseNewline("Collecting and instantiating plugins...", "Plugins");

        foreach (var assembly in assemblies)
        {
            var attrs = assembly.GetCustomAttributes<RegisterCpp2IlPluginAttribute>().ToList();

            if (attrs.Count == 0)
                continue;

            foreach (var registerCpp2IlPluginAttribute in attrs)
            {
                Cpp2IlPlugin plugin;
                try
                {
                    Logger.VerboseNewline($"\tLoading plugin {registerCpp2IlPluginAttribute.PluginType.FullName} from assembly: {assembly.GetName().Name}.dll", "Plugins");
                    plugin = (Cpp2IlPlugin)Activator.CreateInstance(registerCpp2IlPluginAttribute.PluginType)!;
                }
                catch (Exception e)
                {
                    Logger.ErrorNewline($"Plugin {registerCpp2IlPluginAttribute.PluginType.FullName} from assembly {assembly.GetName().Name} threw an exception during construction: {e}. It will not be loaded.", "Plugins");
                    continue;
                }

                _loadedPlugins.Add(plugin);
            }
        }

        Logger.VerboseNewline("Invoking OnLoad on " + _loadedPlugins.Count + " plugins.", "Plugins");
        foreach (var plugin in _loadedPlugins)
        {
            try
            {
                plugin.OnLoad();
                Logger.InfoNewline($"Using Plugin: {plugin.Name}", "Plugins");
            }
            catch (Exception e)
            {
                Logger.ErrorNewline($"Plugin {plugin.GetType().FullName} threw an exception during OnLoad: {e}. It will not receive any further events.", "Plugins");
                _loadedPlugins.Remove(plugin);
            }
        }

        Logger.VerboseNewline("OnLoad complete", "Plugins");
    }

    /// <summary>
    /// Attempts to handle the given game path and populate the runtime arguments by passing them to plugins.
    /// </summary>
    /// <param name="gamePath">The path provided by the user for their game.</param>
    /// <param name="args">The arguments to populate with the result, if the game can be handled</param>
    /// <returns>True if the path was handled, and the game can be loaded based on the arguments, otherwise false.</returns>
    public static bool TryProcessGamePath(string gamePath, ref Cpp2IlRuntimeArgs args)
    {
        foreach (var cpp2IlPlugin in _loadedPlugins)
        {
            if (cpp2IlPlugin.HandleGamePath(gamePath, ref args))
                return true;
        }

        return false;
    }

    public static void CallOnFinish()
    {
        foreach (var cpp2IlPlugin in _loadedPlugins)
        {
            cpp2IlPlugin.CallOnFinish();
        }
    }
}
