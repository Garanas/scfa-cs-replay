using System.Reflection;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// The transaction of the command that is running, for a handler of a command with
/// <see cref="UndoStepAttribute"/>: it edits the map through it.
/// </summary>
public sealed class CurrentEdit
{
    private EditTransaction? _transaction;

    /// <exception cref="InvalidOperationException">The command has no <see cref="UndoStepAttribute"/>.</exception>
    public EditTransaction Transaction
    {
        get => _transaction ?? throw new InvalidOperationException("This command edits the map without [UndoStep]: give its request the attribute");
        internal set => _transaction = value;
    }

    internal void Clear() => _transaction = null;
}

/// <summary>
/// The step of the pipeline that makes a command with <see cref="UndoStepAttribute"/> one step of
/// undo: it opens a transaction named after the command, keeps it when the handler returns and
/// throws it away when the handler fails, so a failing command leaves the history as it was.
/// </summary>
/// <remarks>
/// Transactions do not nest (<see cref="EditSession.Begin"/>): handlers never send commands, and
/// the registry refuses such a command while a brush stroke holds the transaction (<see cref="Needs.NoStroke"/>).
/// </remarks>
public sealed class UndoStepBehavior<TRequest, TResponse>(MainViewModel model, CurrentEdit edit) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly string? Title = typeof(TRequest).GetCustomAttribute<UndoStepAttribute>() is null
        ? null
        : typeof(TRequest).GetCustomAttribute<EditorCommandAttribute>()?.Title.TrimEnd('.') ?? typeof(TRequest).Name;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (Title is null)
        {
            return await next(cancellationToken);
        }
        EditSession session = model.Map?.Session ?? throw new InvalidOperationException("No map is open");
        using EditTransaction transaction = session.Begin(Title);
        edit.Transaction = transaction;
        string status = model.Status;
        bool outer = !model.InCommandStep;
        model.InCommandStep = true;
        try
        {
            TResponse response = await next(cancellationToken);
            transaction.Commit();
            if (model.Status == status)
            {
                // the handler said nothing: the status line tells what was done, not what was before
                model.Status = model.Selection.HasAny ? $"Done: {Title} ({model.Selection.Summary} selected)" : $"Done: {Title}";
            }
            return response;
        }
        finally
        {
            edit.Clear();
            if (outer)
            {
                model.InCommandStep = false;
            }
        }
    }
}
