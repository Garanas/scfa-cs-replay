using System.Text.Json.Nodes;
using FAForever.MapEditor.App.ViewModels;
using MediatR;

namespace FAForever.MapEditor.App.Commands;

/// <summary>
/// Opens the Tours page (user story H-1): the tours of the workspace's <c>tours</c> folder, each with its length, a check
/// once finished and a Start button; a file that cannot be used is listed with its error.
/// </summary>
/// <returns>How many tours can be used.</returns>
[EditorCommand("help.tours", "Tours", Story = "H-1")]
public sealed record ShowTours : IRequest<int>;

public sealed class ShowToursHandler(MainViewModel model) : IRequestHandler<ShowTours, int>
{
    public Task<int> Handle(ShowTours request, CancellationToken cancellationToken)
    {
        model.Tour.OpenPage();
        return Task.FromResult(model.Tour.Items.Count(item => item.CanStart));
    }
}

/// <summary>
/// Starts a tour (user story H-1): the window dims, the step's target is ringed and a card explains it. One that runs ends
/// first. The tour's own commands run without a person; those that leave the map are refused.
/// </summary>
/// <param name="Id">The tour: <c>window</c>.</param>
/// <param name="Step">A step to start at: its number from 1, or its id.</param>
/// <param name="Card">False draws the dimming and the ring without the card (the guide's pictures).</param>
/// <returns>The number of the step that shows.</returns>
[EditorCommand("help.tour", "Start a tour", Story = "H-1")]
public sealed record StartTour(string Id, JsonNode? Step = null, bool Card = true) : IRequest<int>;

public sealed class StartTourHandler(MainViewModel model) : IRequestHandler<StartTour, int>
{
    public async Task<int> Handle(StartTour request, CancellationToken cancellationToken)
    {
        await model.Tour.StartAsync(request.Id, request.Step?.ToString(), request.Card);
        return model.Tour.StepIndex + 1;
    }
}

/// <summary>
/// The tour's next step, or its end at the last one (user story H-1).
/// </summary>
/// <returns>The number of the step that shows; 0 when the tour ended.</returns>
[EditorCommand("help.tourNext", "Next step of the tour", Story = "H-1")]
public sealed record NextTourStep : IRequest<int>;

public sealed class NextTourStepHandler(MainViewModel model) : IRequestHandler<NextTourStep, int>
{
    public async Task<int> Handle(NextTourStep request, CancellationToken cancellationToken)
    {
        await model.Tour.NextAsync();
        return model.Tour.IsRunning ? model.Tour.StepIndex + 1 : 0;
    }
}

/// <summary>
/// The tour's step before (user story H-1).
/// </summary>
/// <returns>The number of the step that shows.</returns>
[EditorCommand("help.tourBack", "Previous step of the tour", Story = "H-1")]
public sealed record PreviousTourStep : IRequest<int>;

public sealed class PreviousTourStepHandler(MainViewModel model) : IRequestHandler<PreviousTourStep, int>
{
    public async Task<int> Handle(PreviousTourStep request, CancellationToken cancellationToken)
    {
        await model.Tour.BackAsync();
        return model.Tour.StepIndex + 1;
    }
}

/// <summary>
/// Ends the tour and puts back what it changed: the view, the mode and tool, the layout (user story H-1).
/// </summary>
/// <returns>Whether a tour ran.</returns>
[EditorCommand("help.tourEnd", "End the tour", Story = "H-1")]
public sealed record EndTour : IRequest<bool>;

public sealed class EndTourHandler(MainViewModel model) : IRequestHandler<EndTour, bool>
{
    public async Task<bool> Handle(EndTour request, CancellationToken cancellationToken)
    {
        bool ran = model.Tour.IsRunning;
        await model.Tour.EndAsync();
        return ran;
    }
}

/// <summary>
/// Does what the tour's step asks of the person (user story H-1): the step's Do it for me.
/// </summary>
/// <returns>Whether what the step waits for holds now.</returns>
[EditorCommand("help.tourDo", "Do the tour's step for me", Story = "H-1")]
public sealed record DoTourStep : IRequest<bool>;

public sealed class DoTourStepHandler(MainViewModel model) : IRequestHandler<DoTourStep, bool>
{
    public async Task<bool> Handle(DoTourStep request, CancellationToken cancellationToken)
    {
        await model.Tour.DoItAsync();
        return model.Tour.IsDone;
    }
}

/// <summary>
/// Answers the card that offers the window's tour once (user story H-1): <c>start</c>, <c>later</c> (offered again
/// next start) or <c>never</c> (Don't ask again); start and never are kept in the workspace.
/// </summary>
/// <param name="Answer"><c>start</c>, <c>later</c> or <c>never</c>.</param>
[EditorCommand("help.tourOffer", "Answer the offer of the tour", Story = "H-1")]
public sealed record AnswerTourOffer(string Answer) : IRequest;

public sealed class AnswerTourOfferHandler(MainViewModel model) : IRequestHandler<AnswerTourOffer>
{
    public Task Handle(AnswerTourOffer request, CancellationToken cancellationToken) =>
        Enum.TryParse(request.Answer, ignoreCase: true, out TourOffer answer) && Enum.IsDefined(answer)
            ? model.Tour.AnswerOfferAsync(answer)
            : throw new ArgumentException($"The answer is start, later or never, not '{request.Answer}'");
}
