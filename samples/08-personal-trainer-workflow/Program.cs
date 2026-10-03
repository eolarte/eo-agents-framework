using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;

var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");
}

var modelName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";
var chatClient = new OpenAIClient(apiKey).GetChatClient(modelName).AsIChatClient();

AIAgent CreateStageAgent(string name, string stageInstructions) => chatClient.AsAIAgent(
    new ChatClientAgentOptions
    {
        Name = name,
        ChatOptions = new ChatOptions { Instructions = stageInstructions }
    });

var warmUp = new WorkoutStageExecutor(
    "WarmUpExecutor",
    CreateStageAgent(
        "WarmUpCoach",
        "You guide the warm-up stage of a gentle, general beginner workout. Give a brief, clear cue for " +
        "marching in place for 30 seconds, followed by 5 comfortable shoulder rolls in each direction. " +
        "Include simple breathing and posture cues. Do not give medical advice; remind the user to stop if uncomfortable."));
var mainWorkout = new WorkoutStageExecutor(
    "MainWorkoutExecutor",
    CreateStageAgent(
        "MainWorkoutCoach",
        "You guide the main stage of a gentle, general beginner workout. Give a brief, clear cue for " +
        "8 chair squats and 8 wall push-ups, with an easy pace and a rest as needed. Include simple form and breathing cues. " +
        "Do not give medical advice; remind the user to stop if uncomfortable."));
var coolDown = new WorkoutStageExecutor(
    "CoolDownExecutor",
    CreateStageAgent(
        "CoolDownCoach",
        "You guide the cool-down stage of a gentle, general beginner workout. Give a brief, clear cue for " +
        "30 seconds of easy walking in place and a comfortable 15-second calf stretch on each side. " +
        "Avoid bouncing or forcing a stretch. Do not give medical advice; remind the user to stop if uncomfortable."));

var builder = new WorkflowBuilder(warmUp);
builder.AddEdge(warmUp, mainWorkout);
builder.AddEdge(mainWorkout, coolDown).WithOutputFrom(coolDown);
var workflow = builder.Build();

Console.WriteLine("Personal trainer workflow");
Console.WriteLine("A gentle demonstration workout: warm-up, main workout, then cool-down.");
Console.WriteLine("This is general exercise demo guidance, not medical advice. Stop if anything feels uncomfortable.");
Console.WriteLine("Press Enter to begin, or type anything else to exit.");
if (!string.IsNullOrEmpty(Console.ReadLine()))
{
    return;
}

await using var run = await InProcessExecution.RunAsync(workflow, new WorkoutState());
foreach (var evt in run.NewEvents)
{
    if (evt is ExecutorCompletedEvent completed && completed.Data is WorkoutState state)
    {
        if (state.Stopped)
        {
            Console.WriteLine("Workout stopped. Take care!");
        }
        else if (completed.ExecutorId == "CoolDownExecutor")
        {
            Console.WriteLine("Workout complete. Nice work!");
        }
    }
}

internal sealed record WorkoutState(bool Stopped = false);

internal sealed class WorkoutStageExecutor : Executor<WorkoutState, WorkoutState>
{
    private const int CountdownSeconds = 10;
    private readonly string _id;
    private readonly AIAgent _agent;

    public WorkoutStageExecutor(string id, AIAgent agent) : base(id)
    {
        _id = id;
        _agent = agent;
    }

    public override async ValueTask<WorkoutState> HandleAsync(
        WorkoutState state,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        if (state.Stopped)
        {
            return state;
        }

        Console.WriteLine($"\n--- {_id.Replace("Executor", string.Empty)} ---");
        var response = await _agent.RunAsync(
            "Give the exercise cues for your stage now. Keep the response concise and easy to follow.",
            cancellationToken: cancellationToken);
        Console.WriteLine(response);
        Console.WriteLine($"Starting a {CountdownSeconds}-second countdown. Press S at any time to stop.");

        for (var remaining = CountdownSeconds; remaining > 0; remaining--)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.S)
                {
                    return new WorkoutState(Stopped: true);
                }
            }

            Console.Write($"\r{remaining} ");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        Console.WriteLine("\rDone.          ");
        return state;
    }
}
