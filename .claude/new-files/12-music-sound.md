# 12 — Music, Sound & Audio

## Audio Direction
- Ambient electronic / lo-fi sci-fi soundtrack
- Satisfying, tactile sound effects for crafting and automation
- Audio should feel rewarding — every craft and unlock should feel good
- Music is **per-screen**, not reactive to build activity

## Music System

### Default Screen Music
| Screen | Default Track Style |
|--------|-------------------|
| Main Menu | Ambient electronic, atmospheric |
| Build Grid | Steady mid-tempo electronic loop |
| Research Panel | Slower, thoughtful ambient |
| Prestige Screen | Rising, triumphant swell |
| PVP Competition | Higher energy, driving beat |
| Codex | Calm, curious ambient |

### Music as Cosmetic
- Additional music tracks unlockable via **achievements and cosmetic rewards**
- Players can **assign any unlocked track to any screen** from the audio settings menu
- Creates meaningful audio personalization — a veteran player's main menu sounds different from a new player's
- Premium music packs available as IAP (additional monetization avenue)

### Music Track Types (Unlockable)
| Pack | Style | Unlock Method |
|------|-------|--------------|
| Default Pack | Ambient electronic | Available from start |
| Deep Space | Dark ambient, drone | Achievement unlock |
| Reactor Core | Industrial electronic | Achievement unlock |
| Organic Lab | Acoustic + electronic hybrid | Achievement unlock |
| Synthwave | Retro 80s synth | Premium IAP |
| Orchestral Sci-Fi | Cinematic, sweeping | Premium IAP |

## Sound Effects

| Event | Sound |
|-------|-------|
| Manual craft tap | Soft click / chime |
| Item produced | Satisfying "pop" or tone |
| Building placed | Mechanical clunk |
| Conveyor running | Subtle mechanical hum (looped) |
| Tier unlocked | Rising swell + chime |
| Prestige triggered | Full triumphant sting |
| Achievement unlocked | Short reward chime |
| Radioactive decay | Low hum + crackle |
| Power offline warning | Soft alert tone |
| Error / no resource | Soft negative buzz |
| Grid expansion | Spatial whoosh + click |

## Implementation
- **Unity Audio System** (AudioSource + AudioMixer)
- Separate mixer groups: Music, SFX, UI — individually volume-controlled in settings
- Music tracks streamed, SFX loaded in memory
- Per-screen music manager — loads assigned track on screen transition, crossfades smoothly
- Player music assignments stored in save data (persists through prestige)

## Haptics
- Light haptic feedback on manual craft taps (iOS Taptic Engine / Android Vibrator API)
- Medium haptic on tier unlock and prestige
- Haptics toggleable in settings independently of audio

## Notes / Open Questions
- [ ] Commission original music vs licensed tracks?
- [ ] Define crossfade duration between screen music tracks
- [ ] Haptic feedback library — Unity's built-in or third-party (Lofelt recommended for mobile)
