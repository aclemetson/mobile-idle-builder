# 10 — PVP / Competitive Features

## Vision
Async competitive events where players race to build the highest net worth within a fixed time window — inspired by Idle Planet Miner's competition format. No real-time conflict, no direct interference — pure build optimization under a timer.

## PVP Format

### Weekly Competition
- **Entry window:** 24 hours starting Friday — players can begin anytime within this window
- **Build window:** 48 hours from when the player starts their competition run
- **Goal:** Achieve the highest possible build net worth before time expires
- **Scoring:** Net worth calculated server-side at the end of the 48-hour window
- **Rewards:** Ranked rewards distributed after all runs complete (Sunday/Monday)

### Competition Run Rules
- Players start a fresh build (same as a prestige run) — grid resets, inventory cleared
- **Permanent multipliers carry in** — veterans have an edge, reflecting real progression investment
- All players compete in the same pool unless bracket/league system is added later
- Net worth formula is identical to the prestige net worth calculation — consistent and familiar

### Unlock Gate
- PVP mode is locked until the player has completed a **minimum number of prestiges** (TBD — likely 2-3)
- Ensures players understand the core loop before competing
- Gate shown clearly in the UI — "Complete X prestiges to unlock Weekly Competition"

## Matchmaking & Leagues
- Initial launch: single global leaderboard
- Future: league system (Bronze → Silver → Gold → Platinum) based on historical competition scores
- Season resets every month — league standings reset, rewards distributed

## Rewards
| Placement | Reward |
|-----------|--------|
| Top 1% | Exclusive cosmetic + large prestige currency bonus |
| Top 10% | Rare cosmetic + prestige currency bonus |
| Top 25% | Prestige currency bonus |
| Top 50% | Small prestige currency bonus |
| Participated | Participation badge |

## Anti-Cheat
- Net worth calculated and verified **server-side via AWS Lambda** — client never self-reports score
- Production rate claims validated against known building configs and time elapsed
- Anomalous scores flagged for review before rewards distributed
- Rate limiting on API calls during competition window

## Notes / Open Questions
- [ ] Define exact prestige count unlock gate
- [ ] Define net worth formula precisely (see 03-buildings-automation.md)
- [ ] Add league system in v1 or post-launch?
- [ ] Push notification when competition window opens Friday
