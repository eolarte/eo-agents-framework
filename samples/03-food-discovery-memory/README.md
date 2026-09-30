# Food discovery with persistent memory

This sample demonstrates a user-scoped memory component built with
`AIContextProvider`:

1. The console asks for the customer's name and selects their local profile.
2. A dedicated extraction client captures the customer's food preferences as
   structured data.
3. The memory provider loads or updates that customer's JSON profile.
4. The agent uses the remembered profile while searching the in-memory menu
   and preparing a local order draft.
5. Confirmed local orders are added to the customer's profile history.

The remembered profile includes:

- Dietary preferences or restrictions.
- Favorite cuisines.
- Disliked foods or ingredients.
- Spice tolerance.
- Typical budget.
- Confirmed orders, including structured menu item details, requested variation
  outcomes, and the UTC confirmation time.

Budget values are only persisted when the customer explicitly provides a
realistic non-negative number; malformed or out-of-range extraction values are
ignored rather than terminating the conversation.

Profiles are stored as JSON files under the user's OS-specific local
application-data directory:

```text
Microsoft/AgentFramework/food-discovery-memory/users/<safe-customer-name>.json
```

The profile data can contain personal preferences and should be treated as
sensitive. This is a local demonstration, not a production profile store.
Customers with the same name intentionally share a profile in this sample.

Conversation history is scoped to the current process. Type `reset` to start a
new conversation and select a profile again while keeping persisted profiles.

The sample never places a real order, processes payment, or connects to an
external restaurant system. Confirmation records the order locally in the
customer's profile. Pending and cancelled drafts are not added to order history.

## Run with OpenAI

```bash
export AZURE_OPENAI_API_KEY="your-api-key"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini" # optional
dotnet run --project samples/03-food-discovery-memory/food-discovery-memory.csproj
```

## Run with Azure AI Foundry

```bash
export AZURE_OPENAI_ENDPOINT="https://your-project-endpoint"
export AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o-mini" # optional
az login
dotnet run --project samples/03-food-discovery-memory/food-discovery-memory.csproj
```

## Example flow

1. Start the sample and answer the agent's welcome question with your name.
2. Tell the agent preferences such as “I like Japanese food, avoid spicy
   dishes, and usually spend under 20.”
3. Ask for a recommendation. The agent searches the menu and uses the saved
   preferences as context.
4. Exit and run the sample again with the same name. The JSON profile is loaded
   and the preferences are available after the name is provided again.
5. Use `status`, `confirm`, or `cancel` to inspect and change the local order
   draft. Confirmed orders appear in the `OrderHistory` array of the profile JSON.

Each confirmed history entry contains its menu dishes with menu item ID, name,
description, and price. Customer-requested variations are recorded with an
`applied`, `declined`, or `unresolved` outcome. A declined change remains in the
variation record while the saved dish reflects the menu item as listed. History
entries written by earlier versions with transcript fields are not migrated to
the new structured format.
