# Travel agency intake with file memory

This sample uses a conversational travel agent to build an ongoing traveler
profile. The agent asks one intake question at a time about preferred
destinations, travel style, budget, pace, lodging, and dietary or accessibility
needs. It stores the profile with `FileMemoryProvider` and can use it in later
conversations.

The sample uses a fixed demo traveler ID, so each run shares the same profile.
Memory files are stored under `agent-memory/travelers/demo-traveler` beside the
built application. The profile can contain personal preferences; this local
file-based store is for demonstration and is not a production profile service.

## Run

Set a direct OpenAI API key and, optionally, the model name:

```bash
export AZURE_OPENAI_API_KEY="your-api-key"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini" # optional
dotnet run --project samples/04-travel-agency/travel-agency.csproj
```

The sample requires `AZURE_OPENAI_API_KEY` and reports an error if it is
missing. Enter `exit` or press Enter to end the conversation. On the next run,
the agent can load the saved profile and ask whether the traveler wants to
update it.
