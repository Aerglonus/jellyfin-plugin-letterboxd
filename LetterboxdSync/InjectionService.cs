using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LetterboxdSync;

/// <summary>
/// A hosted service that automatically injects the Letterboxd menu script
/// into the Jellyfin web client's index.html at startup.
/// This completely eliminates the need for users to manually configure
/// the third-party JavaScript Injector plugin.
/// </summary>
public class InjectionService : IHostedService
{
    private readonly ILogger<InjectionService> _logger;
    private readonly IApplicationPaths _appPaths;

    public InjectionService(ILogger<InjectionService> logger, IApplicationPaths appPaths)
    {
        _logger = logger;
        _appPaths = appPaths;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        InjectScript();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void InjectScript()
    {
        try
        {
            if (TryRegisterFileTransformation())
            {
                _logger.LogInformation("[LetterboxdSync] Successfully registered index.html injection via File Transformation plugin.");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LetterboxdSync] Failed to register with File Transformation plugin. Falling back to disk modification.");
        }

        try
        {
            var indexPath = Path.Combine(_appPaths.WebPath, "index.html");
            if (!File.Exists(indexPath))
            {
                _logger.LogWarning("[LetterboxdSync] Could not find index.html at path: {Path}. Menu script will not be auto-injected.", indexPath);
                return;
            }

            var content = File.ReadAllText(indexPath);

            var startComment = "<!-- BEGIN LetterboxdSync Menu Injector -->";
            var endComment = "<!-- END LetterboxdSync Menu Injector -->";
            var scriptUrl = "../Jellyfin.Plugin.LetterboxdSync/MenuScript";
            var injectionBlock = $"{startComment}\n<script src=\"{scriptUrl}\"></script>\n{endComment}";

            if (content.Contains(startComment))
            {
                var start = content.IndexOf(startComment, StringComparison.Ordinal);
                var end = content.IndexOf(endComment, start, StringComparison.Ordinal);
                if (end >= 0)
                {
                    end += endComment.Length;
                    content = content.Remove(start, end - start).Insert(start, injectionBlock);
                    File.WriteAllText(indexPath, content);
                    _logger.LogInformation("[LetterboxdSync] Refreshed menu script injection in index.html.");
                    return;
                }
            }

            var closingBodyTag = "</body>";
            if (content.Contains(closingBodyTag))
            {
                content = content.Replace(closingBodyTag, $"{injectionBlock}\n{closingBodyTag}");
                File.WriteAllText(indexPath, content);
                _logger.LogInformation("[LetterboxdSync] Successfully self-injected menu script into index.html.");
            }
            else
            {
                _logger.LogWarning("[LetterboxdSync] Could not find </body> tag in index.html. Scripts not injected.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LetterboxdSync] Error while trying to inject script block into index.html.");
        }
    }

    private bool TryRegisterFileTransformation()
    {
        var fileTransformationAssembly = AssemblyLoadContext.All
            .SelectMany(x => x.Assemblies)
            .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation") ?? false);

        if (fileTransformationAssembly == null)
            return false;

        var pluginInterfaceType = fileTransformationAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
        if (pluginInterfaceType == null)
            return false;

        var jObjectType = Type.GetType("Newtonsoft.Json.Linq.JObject, Newtonsoft.Json") 
            ?? AssemblyLoadContext.All.SelectMany(x => x.Assemblies).FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json")?.GetType("Newtonsoft.Json.Linq.JObject");

        if (jObjectType == null)
            return false;

        var parseMethod = jObjectType.GetMethod("Parse", new[] { typeof(string) });
        if (parseMethod == null)
            return false;

        var payloadJson = JsonSerializer.Serialize(new
        {
            id = Guid.Parse("00f8df7c-b110-4529-8e8c-7675905cfd0f").ToString(), // Re-use plugin ID for transformation ID
            fileNamePattern = "index\\.html",
            callbackAssembly = GetType().Assembly.FullName,
            callbackClass = GetType().FullName,
            callbackMethod = nameof(TransformIndexHtml)
        });

        var payloadObj = parseMethod.Invoke(null, new object[] { payloadJson });
        var registerMethod = pluginInterfaceType.GetMethod("RegisterTransformation");
        
        if (registerMethod != null && payloadObj != null)
        {
            registerMethod.Invoke(null, new[] { payloadObj });
            return true;
        }

        return false;
    }

    public static string TransformIndexHtml(Dictionary<string, string> payload)
    {
        if (payload == null || !payload.TryGetValue("contents", out var content) || string.IsNullOrEmpty(content))
            return payload != null && payload.ContainsKey("contents") ? payload["contents"] : string.Empty;

        var startComment = "<!-- BEGIN LetterboxdSync Menu Injector -->";
        var endComment = "<!-- END LetterboxdSync Menu Injector -->";
        var scriptUrl = "../Jellyfin.Plugin.LetterboxdSync/MenuScript";
        var injectionBlock = $"{startComment}\n<script src=\"{scriptUrl}\"></script>\n{endComment}";

        if (content.Contains(startComment))
        {
            var start = content.IndexOf(startComment, StringComparison.Ordinal);
            var end = content.IndexOf(endComment, start, StringComparison.Ordinal);
            if (end >= 0)
            {
                end += endComment.Length;
                content = content.Remove(start, end - start).Insert(start, injectionBlock);
                return content;
            }
        }

        var closingBodyTag = "</body>";
        if (content.Contains(closingBodyTag))
        {
            content = content.Replace(closingBodyTag, $"{injectionBlock}\n{closingBodyTag}");
        }

        return content;
    }
}
