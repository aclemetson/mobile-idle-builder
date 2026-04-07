# 08 — Online Storage & Cloud Save

## Backend: AWS

### Why AWS over Unity Cloud Save
- Full control over data schema and sync logic
- Significantly cheaper at scale
- Required for server-side PVP validation and game logic
- Real-time capabilities via AppSync/WebSockets
- Integrates with Cognito for Apple/Google SSO
- Push notifications via SNS
- Serverless game logic via Lambda

## AWS Service Map
| Service | Role |
|---------|------|
| **DynamoDB** | Primary game state store (player data, grid, prestige) |
| **S3** | Bulk storage (codex assets, patch data, backups) |
| **Cognito** | Authentication — Apple Sign-In, Google Sign-In |
| **Lambda** | Server-side game logic (PVP validation, prestige net worth verification) |
| **API Gateway** | REST endpoints for client ↔ Lambda communication |
| **AppSync** | Real-time / WebSocket layer for PVP and leaderboards |
| **SNS** | Push notifications (prestige ready, PVP results, events) |
| **CloudWatch** | Logging, monitoring, alerting |

## Infrastructure as Code

### Tooling
- **Terraform** — primary IaC tool for all AWS resources
- **Dockerfile** — containerized Lambda functions where applicable
- **Makefile** — single entry point for common dev/deploy commands

### Repository Structure
```
infra/
  terraform/
    modules/
      dynamodb/         # Table definitions
      lambda/           # Function configs
      api-gateway/      # API routes
      cognito/          # Auth pools
      appsync/          # Real-time layer
      sns/              # Push notifications
    environments/
      dev/
        main.tf
        variables.tf
        terraform.tfvars
      staging/
        main.tf
        variables.tf
        terraform.tfvars
      prod/
        main.tf
        variables.tf
        terraform.tfvars
  docker/
    lambda/             # Dockerfiles for Lambda containers
Makefile                # Top-level command runner
```

### Makefile Commands
```makefile
# Deploy to a specific environment
make deploy ENV=dev
make deploy ENV=staging
make deploy ENV=prod

# Destroy environment (dev/staging only)
make destroy ENV=dev

# Run Lambda locally
make local-lambda FUNCTION=prestige-validator

# Apply Terraform plan without deploying
make plan ENV=staging
```

## Environments
| Environment | Purpose | Data |
|-------------|---------|------|
| `dev` | Local development, AI-assisted builds | Seeded test data |
| `staging` | Pre-release testing, beta builds | Anonymised prod-like data |
| `prod` | Live game | Real player data |

- Each environment is fully isolated — separate DynamoDB tables, Cognito pools, API endpoints
- Environment selected at Unity build time via a config ScriptableObject (never hardcoded)
- Staging mirrors prod infrastructure exactly — no surprises on release

## Save Data Structure (Draft)
```json
{
  "playerId": "uid_123",
  "lastSaved": "2026-04-06T12:00:00Z",
  "prestigeCount": 4,
  "prestigeCurrency": 1240,
  "permanentUpgrades": ["vault_capacity_1", "lab_speed_1"],
  "unlockedRecipes": ["hydrogen", "deuterium", "water"],
  "unlockedResearch": ["carbon_dating", "nuclear_fusion"],
  "currentRun": {
    "baseCurrency": 5400,
    "inventory": { "hydrogen": 240, "oxygen": 120 },
    "nonPersistentUpgrades": ["conveyor_speed_1"],
    "grid": {
      "size": { "x": 12, "y": 12 },
      "expansions": [{ "x": 12, "y": 0, "size": 6 }],
      "buildings": [
        { "type": "basic_combiner", "position": [2, 4], "level": 1 },
        { "type": "conveyor", "from": [2, 4], "to": [3, 4] },
        { "type": "power_node", "position": [5, 5], "level": 2 }
      ]
    },
    "researchProgress": { "semiconductors": 0.6 }
  },
  "codex": ["hydrogen", "deuterium", "water", "hcl"],
  "achievements": ["first_atom", "first_molecule"]
}
```

### Grid Persistence Rules
- **Within a run:** Full grid state saved continuously and restored exactly on app reopen
- **On prestige:** `currentRun` block is cleared entirely — grid wipes as part of the intentional reset
- **Persistent data** (prestige currency, permanent upgrades, unlocked recipes, codex) survives prestige and is never cleared

## Offline & Sync Strategy
- Full save stored locally on device (JSON via Unity's persistent data path)
- On reconnect: compare server timestamp vs local, take newest
- Conflict resolution: server wins, local kept as backup for 24hrs
- Offline production calculated server-side on next sync via Lambda (prevents cheating)

## Notes / Open Questions
- [ ] Define DynamoDB partition key strategy (playerId + saveVersion recommended)
- [ ] Decide on sync frequency — every 60s, on app background, or on significant events only
- [ ] Set AWS region — us-east-1 recommended as primary, with replication for global launch
