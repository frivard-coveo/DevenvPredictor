using System.Management.Automation.Subsystem;
using System.Management.Automation;
using System.Management.Automation.Subsystem.Prediction;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Language;
using System.Diagnostics.CodeAnalysis;

namespace DevenvPredictor;

public class Predictor : ICommandPredictor, IDisposable
{
    const string TrappedCommandName = "devenv";

    private readonly Guid _guid;
    public Guid Id => _guid;
    private string? _cwd;

    internal Predictor(string guid)
    {
        _guid = new Guid(guid);
        RegisterEvents();
    }

    public void Dispose()
    {
        UnregisterEvents();
    }

    public string Name => "Devenv";

    public string Description => "Suggests solution files dans csharp project files to open";

    public SuggestionPackage GetSuggestion(PredictionClient client, PredictionContext context, CancellationToken cancellationToken)
    {
        if(_cwd is null) { return default; }
        
        List<PredictiveSuggestion>? list = null;
        Token? tokenAtCursor = context.TokenAtCursor;
        IReadOnlyList<Ast> relatedAsts = context.RelatedAsts;

        bool isCommandAstWithLiteralName = IsCommandAstWithLiteralName(context, out CommandAst? cmdAst, out StringConstantExpressionAst? nameAst);
        if(isCommandAstWithLiteralName && cmdAst!=null)
        {
            bool isDevenv = string.Equals(TrappedCommandName, cmdAst.GetCommandName(), StringComparison.OrdinalIgnoreCase);
            if (isDevenv)
            {
                if (tokenAtCursor is null)
                {
                    if (cmdAst.CommandElements.Count == 1)
                    {
                        list = Directory.EnumerateFiles(_cwd, "*.sln", SearchOption.TopDirectoryOnly)
                            .Concat(Directory.EnumerateFiles(_cwd, "*.csproj", SearchOption.TopDirectoryOnly))
                            .Select(f => new PredictiveSuggestion(context.InputAst.Extent.Text + Path.GetFileName(f)))
                            .ToList();
                    }
                }
                else
                {
                    if (cmdAst.CommandElements.Count == 2)
                    {
                        string prefix = cmdAst.CommandElements[1].Extent.Text;
                        string trimmedCommand = context.InputAst.Extent.Text.Substring(0, context.InputAst.Extent.Text.Length - prefix.Length);
                        list = Directory.EnumerateFiles(_cwd, $"{prefix}*.sln", SearchOption.TopDirectoryOnly)
                            .Concat(Directory.EnumerateFiles(_cwd, $"{prefix}*.csproj", SearchOption.TopDirectoryOnly))
                            .Select(f => new PredictiveSuggestion(trimmedCommand + Path.GetFileName(f)))
                            .ToList();
                    }
                }
            }
        }
        return list is null ? default : new SuggestionPackage(list);
    }

    private bool IsCommandAstWithLiteralName(
        PredictionContext context,
        [NotNullWhen(true)] out CommandAst? cmdAst,
        [NotNullWhen(true)] out StringConstantExpressionAst? nameAst)
    {
        Ast lastAst = context.RelatedAsts[^1];
        cmdAst = lastAst.Parent as CommandAst;
        nameAst = cmdAst?.CommandElements[0] as StringConstantExpressionAst;
        return nameAst is not null;
    }

    private void RegisterEvents()
    {
        Runspace.DefaultRunspace.AvailabilityChanged += SyncRunspaceState;
    }

    private void UnregisterEvents()
    {
        Runspace.DefaultRunspace.AvailabilityChanged -= SyncRunspaceState;
    }

    private void SyncRunspaceState(object? sender, RunspaceAvailabilityEventArgs e)
    {
        if (sender is null || e.RunspaceAvailability != RunspaceAvailability.Available)
        {
            return;
        }

        var pwshRunspace = (Runspace)sender;
        _cwd = pwshRunspace.SessionStateProxy.Path.CurrentFileSystemLocation.ProviderPath;
    }
}


/// <summary>
/// Register the predictor on module loading and unregister it on module un-loading.
/// </summary>
public class Init : IModuleAssemblyInitializer, IModuleAssemblyCleanup
{
    private const string Identifier = "e32e6bbe-ef86-438a-8142-d236ee32c2d5";

    /// <summary>
    /// Gets called when assembly is loaded.
    /// </summary>
    public void OnImport()
    {
        var predictor = new Predictor(Identifier);
        SubsystemManager.RegisterSubsystem(SubsystemKind.CommandPredictor, predictor);
    }

    /// <summary>
    /// Gets called when the binary module is unloaded.
    /// </summary>
    public void OnRemove(PSModuleInfo psModuleInfo)
    {
        SubsystemManager.UnregisterSubsystem(SubsystemKind.CommandPredictor, new Guid(Identifier));
    }
}
