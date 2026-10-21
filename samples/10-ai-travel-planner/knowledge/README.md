# Demo destination knowledge

These Markdown files are sample source material for the Knowledge branch. The
travel planner loads the six city guides into memory when the server starts.
The branch returns `not_found` when a city and country have no matching guide.

The index includes only the six city files. It excludes this README. Each city
has one file and one stable document ID. The server matches the city and
country together. It selects short sections and returns their text and source
IDs. The section evidence includes the source URL and check date.

The destination facts come from the linked tourism sites. The planning notes
are demo editorial guidance. They are not live data. Before a real trip, check
opening times, prices, tickets, transit service, and access at the provider.
Do not use these files as evidence for weather, travel time, or crowd levels.

| File | Document ID | Destination |
| --- | --- | --- |
| `paris-france.md` | `demo-knowledge:paris-fr` | Paris, France |
| `london-united-kingdom.md` | `demo-knowledge:london-gb` | London, United Kingdom |
| `berlin-germany.md` | `demo-knowledge:berlin-de` | Berlin, Germany |
| `rome-italy.md` | `demo-knowledge:rome-it` | Rome, Italy |
| `madrid-spain.md` | `demo-knowledge:madrid-es` | Madrid, Spain |
| `amsterdam-netherlands.md` | `demo-knowledge:amsterdam-nl` | Amsterdam, Netherlands |
