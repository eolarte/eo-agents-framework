# Personal trainer workflow

This console sample demonstrates a sequential Microsoft Agent Framework
workflow with a separate executor and AI coach for each workout stage:

1. Warm-up: marching in place and shoulder rolls.
2. Main workout: chair squats and wall push-ups.
3. Cool-down: easy walking and a calf stretch.

Each stage agent gives concise exercise, repetition, and pacing cues. Its
executor then runs a ten-second live countdown before handing control to the
next stage. Press `S` during a countdown to stop the workflow. The remaining
executors detect the stopped state and exit without displaying more exercise
cues.

The workout is a low-intensity demonstration with fixed exercises. It does not
assess fitness, adapt the plan to an individual, or provide medical advice. Stop
if anything feels uncomfortable.

## Run

Set a direct OpenAI API key and, optionally, a model name:

```bash
export AZURE_OPENAI_API_KEY="your-api-key"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini" # optional
dotnet run --project samples/08-personal-trainer-workflow/personal-trainer-workflow.csproj
```

The sample requires `AZURE_OPENAI_API_KEY` and reports an error if it is
missing. Press Enter at startup to begin. The three stages run in order, and
the workflow prints a completion message after the cool-down.
