# Food ordering sample

This sample demonstrates a basic multi-agent food-ordering flow:

1. `MenuAgent` searches a structured in-memory menu using a function tool and suggests menu items.
2. `OrderAgent` creates an order summary and asks the customer to confirm it.
3. Each agent uses an `AgentSession` to remember relevant preferences and conversation context.
4. The console tracks draft, confirmation, cancellation, and local completion states.

The sample does not place real orders or connect to an external restaurant menu.
The two agent sessions and the current local order state are serialized to the
user's OS-specific application-data directory so they can be restored after a
process restart. The saved state may contain conversation content and should be
treated as sensitive. Use `reset` to delete the saved state and start a fresh
conversation.

The menu search tool matches dish names, categories, descriptions, ingredients, and dietary tags.
After the order agent produces a draft, use:

- `yes` or `confirm` to confirm the draft.
- `no` or `cancel` to cancel it.
- `status` to display the current order state and summary.
- `complete` after confirmation to simulate local fulfillment.
- `reset` to delete the saved sessions and local order state.

Confirmation and completion are local sample state transitions; they never place an external order.

## Run with OpenAI

```bash
export AZURE_OPENAI_API_KEY="your-api-key"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini" # optional
dotnet run --project samples/02-food-ordering/food-ordering.csproj
```

## Run with Azure AI Foundry

```bash
export AZURE_OPENAI_ENDPOINT="https://your-project-endpoint"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini" # optional
az login
dotnet run --project samples/02-food-ordering/food-ordering.csproj
```

## Possible next iterations

- Add delivery address and payment-validation agents.
- Replace the in-memory menu with a restaurant API or database.
