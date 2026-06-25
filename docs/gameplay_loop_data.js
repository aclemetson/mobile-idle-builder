// docs/gameplay_loop_data.js
// Single source of truth for all gameplay-loop.html documentation data.
// Loaded via <script src="gameplay_loop_data.js"> — works with file:// protocol.
//
// ── TABLE OF CONTENTS ──────────────────────────────────────────────────────
//  1. config            4 economy scalars  (sync with game_data.json game_config)
//  2. items            51 items            (sync with game_data.json items[].base_sell_value)
//  3. recipes          49 recipes          (sync with game_data.json recipes)
//  4. buildings        10 buildings        (sync with game_data.json buildings[].entropy_cost)
//  5. research         20 nodes            (sync with game_data.json research[].cost_base_currency)
//  6. tutorial_phases   6 phases / 50 steps with doc annotations (incl. upgrade tutorial ⑥ cont.)
//  7. post_tutorial_phases  9 phases (Runs 2+, phases 7–15)
//  8. dialogues        12 phase groups / ~40 dialogue entries
//  9. design_gaps      13 tracked issues
// 10. simple_overview  18 phases for the Simple Overview tab
// 11. prestige         formula params + 10 permanent upgrades
// ────────────────────────────────────────────────────────────────────────────
window.LOOP_DATA = {

// ─────────────────────────────────────── CONFIG ──
config: {
  assemblerEvPerMass: 5.0,
  manipulatorEvPerNeutron: 8.0,
  prestigeBaseValue: 5000,
  startingEntropy: 250,
},

// ─────────────────────────────────────── ITEMS ──
// Sell values follow a ×2.0 ladder anchored at H=5e. H stays at 5e to preserve tutorial balance.
items: [
  // Tier 1 — Subatomic
  { id:'up_quark',                 label:'Up Quark',                sym:'u',     cat:'Raw',       tier:1, sell:1,            mult:1.0 },
  { id:'down_quark',               label:'Down Quark',               sym:'d',     cat:'Raw',       tier:1, sell:1,            mult:1.0 },
  { id:'electron',                 label:'Electron',                 sym:'e⁻',    cat:'Raw',       tier:1, sell:1,            mult:1.0 },
  { id:'proton',                   label:'Proton',                   sym:'p⁺',    cat:'Nucleon',   tier:1, sell:3,            mult:1.0 },
  { id:'neutron',                  label:'Neutron',                  sym:'n⁰',    cat:'Nucleon',   tier:1, sell:3,            mult:1.0 },
  // Tier 2 — Elements (×2 ladder: H=5, He=10, Li=20 … Pu=2,621,440)
  { id:'hydrogen',                 label:'Hydrogen',                 sym:'H',     cat:'Element',   tier:2, sell:5,            mult:1.0 },
  { id:'helium_4',                 label:'Helium-4',                 sym:'He',    cat:'Element',   tier:2, sell:10,           mult:1.0 },
  { id:'lithium',                  label:'Lithium',                  sym:'Li',    cat:'Element',   tier:2, sell:20,           mult:1.0 },
  { id:'beryllium',                label:'Beryllium',                sym:'Be',    cat:'Element',   tier:2, sell:40,           mult:1.0 },
  { id:'boron',                    label:'Boron',                    sym:'B',     cat:'Element',   tier:2, sell:80,           mult:1.0 },
  { id:'carbon',                   label:'Carbon',                   sym:'C',     cat:'Element',   tier:2, sell:160,          mult:1.0 },
  { id:'nitrogen',                 label:'Nitrogen',                 sym:'N',     cat:'Element',   tier:2, sell:320,          mult:1.0 },
  { id:'oxygen',                   label:'Oxygen',                   sym:'O',     cat:'Element',   tier:2, sell:640,          mult:1.0 },
  { id:'silicon',                  label:'Silicon',                  sym:'Si',    cat:'Element',   tier:2, sell:1280,         mult:1.0 },
  { id:'aluminum',                 label:'Aluminum',                 sym:'Al',    cat:'Element',   tier:2, sell:2560,         mult:1.0 },
  { id:'iron',                     label:'Iron',                     sym:'Fe',    cat:'Element',   tier:2, sell:5120,         mult:1.0 },
  { id:'nickel',                   label:'Nickel',                   sym:'Ni',    cat:'Element',   tier:2, sell:10240,        mult:1.0 },
  { id:'copper',                   label:'Copper',                   sym:'Cu',    cat:'Element',   tier:2, sell:20480,        mult:1.0 },
  { id:'zinc',                     label:'Zinc',                     sym:'Zn',    cat:'Element',   tier:2, sell:40960,        mult:1.0 },
  { id:'silver',                   label:'Silver',                   sym:'Ag',    cat:'Element',   tier:2, sell:81920,        mult:1.0 },
  { id:'gold',                     label:'Gold',                     sym:'Au',    cat:'Element',   tier:2, sell:163840,       mult:1.0 },
  { id:'platinum',                 label:'Platinum',                 sym:'Pt',    cat:'Element',   tier:2, sell:327680,       mult:1.0 },
  { id:'tungsten',                 label:'Tungsten',                 sym:'W',     cat:'Element',   tier:2, sell:655360,       mult:1.0 },
  { id:'uranium',                  label:'Uranium',                  sym:'U',     cat:'Element',   tier:2, sell:1310720,      mult:1.0 },
  { id:'plutonium',                label:'Plutonium',                sym:'Pu',    cat:'Element',   tier:2, sell:2621440,      mult:1.0 },
  // Tier 2 — Isotopes
  { id:'deuterium',                label:'Deuterium',                sym:'H-2',   cat:'Isotope',   tier:2, sell:8,            mult:1.5 },
  { id:'tritium',                  label:'Tritium',                  sym:'H-3',   cat:'Isotope',   tier:2, sell:12,           mult:1.5 },
  { id:'carbon_14',                label:'Carbon-14',                sym:'C-14',  cat:'Isotope',   tier:2, sell:240,          mult:1.5 },
  { id:'uranium_235',              label:'Uranium-235',              sym:'U-235', cat:'Isotope',   tier:2, sell:2621440,      mult:2.0 },
  // Tier 2 — Particles
  { id:'alpha_particle',           label:'Alpha Particle',           sym:'α',     cat:'Particle',  tier:2, sell:50,           mult:1.0 },
  { id:'beta_particle',            label:'Beta Particle',            sym:'β',     cat:'Particle',  tier:2, sell:30,           mult:1.0 },
  // Tier 3 — Molecules
  { id:'liquid_hydrogen',          label:'Liquid Hydrogen',          sym:'LH₂',   cat:'Molecule',  tier:3, sell:300,          mult:1.0 },
  { id:'water',                    label:'Water',                    sym:'H₂O',   cat:'Molecule',  tier:3, sell:6000,         mult:1.0 },
  { id:'methane',                  label:'Methane',                  sym:'CH₄',   cat:'Molecule',  tier:3, sell:5000,         mult:1.0 },
  { id:'ammonia',                  label:'Ammonia',                  sym:'NH₃',   cat:'Molecule',  tier:3, sell:10000,        mult:1.0 },
  { id:'silica',                   label:'Silica',                   sym:'SiO₂',  cat:'Molecule',  tier:3, sell:50000,        mult:1.0 },
  { id:'iron_oxide',               label:'Iron Oxide',               sym:'Fe₂O₃', cat:'Molecule',  tier:3, sell:200000,       mult:1.0 },
  { id:'uranium_hexafluoride',     label:'Uranium Hexafluoride',     sym:'UF₆',   cat:'Molecule',  tier:3, sell:8000000,      mult:1.0 },
  // Tier 4 — Materials
  { id:'steel',                    label:'Steel',                    sym:'Fe·C',  cat:'Alloy',     tier:4, sell:1500000,      mult:1.0 },
  { id:'carbon_fiber',             label:'Carbon Fiber',             sym:'C-F',   cat:'Alloy',     tier:4, sell:6000000,      mult:1.0 },
  { id:'titanium_alloy',           label:'Titanium Alloy',           sym:'Ti-A',  cat:'Alloy',     tier:4, sell:25000000,     mult:1.0 },
  { id:'semiconductor_wafer',      label:'Semiconductor Wafer',      sym:'Si-W',  cat:'Alloy',     tier:4, sell:100000000,    mult:1.0 },
  { id:'aerogel',                  label:'Aerogel',                  sym:'SiO₂-A',cat:'Alloy',     tier:4, sell:400000000,    mult:1.0 },
  { id:'superconductor',           label:'Superconductor',           sym:'SC',    cat:'Alloy',     tier:4, sell:1600000000,   mult:1.0 },
  { id:'metamaterial',             label:'Metamaterial',             sym:'MM',    cat:'Alloy',     tier:4, sell:6400000000,   mult:1.0 },
  // Tier 5 — Components
  { id:'quantum_processor',        label:'Quantum Processor',        sym:'QP',    cat:'Component', tier:5, sell:50000000000,  mult:1.0 },
  { id:'plasma_containment_ring',  label:'Plasma Containment Ring',  sym:'PCR',   cat:'Component', tier:5, sell:200000000000, mult:1.0 },
  { id:'antimatter_cell',          label:'Antimatter Cell',          sym:'AM',    cat:'Component', tier:5, sell:800000000000, mult:1.0 },
  { id:'dyson_node',               label:'Dyson Node',               sym:'DN',    cat:'Component', tier:5, sell:5e12,         mult:1.0 },
  { id:'orbital_frame',            label:'Orbital Frame',            sym:'OF',    cat:'Component', tier:5, sell:2e13,         mult:1.0 },
  { id:'graviton_lens',            label:'Graviton Lens',            sym:'GL',    cat:'Component', tier:5, sell:1e14,         mult:1.0 },
],

// ─────────────────────────────────────── RECIPES ──
recipes: [
  // Tier 1 — Nucleons (SFC)
  { id:'proton',                   label:'Proton',                   building:'SFC',          inputs:'2u + 1d',               time:1,    powerEV:0,        sellId:'proton' },
  { id:'neutron',                  label:'Neutron',                  building:'SFC',          inputs:'1u + 2d',               time:1,    powerEV:0,        sellId:'neutron' },
  // Tier 2 — Elements (Assembler — power = atomic_mass × 5 eV)
  { id:'hydrogen',                 label:'Hydrogen',                 building:'Assembler',    inputs:'1p + 1e',               time:2,    powerEV:5,        sellId:'hydrogen' },
  { id:'helium_4',                 label:'Helium-4',                 building:'Assembler',    inputs:'2p + 2n + 2e',          time:4,    powerEV:20,       sellId:'helium_4' },
  { id:'lithium',                  label:'Lithium',                  building:'Assembler',    inputs:'3p + 4n + 3e',          time:7,    powerEV:35,       sellId:'lithium' },
  { id:'beryllium',                label:'Beryllium',                building:'Assembler',    inputs:'4p + 5n + 4e',          time:9,    powerEV:45,       sellId:'beryllium' },
  { id:'boron',                    label:'Boron',                    building:'Assembler',    inputs:'5p + 6n + 5e',          time:11,   powerEV:55,       sellId:'boron' },
  { id:'carbon',                   label:'Carbon',                   building:'Assembler',    inputs:'6p + 6n + 6e',          time:12,   powerEV:60,       sellId:'carbon' },
  { id:'nitrogen',                 label:'Nitrogen',                 building:'Assembler',    inputs:'7p + 7n + 7e',          time:14,   powerEV:70,       sellId:'nitrogen' },
  { id:'oxygen',                   label:'Oxygen',                   building:'Assembler',    inputs:'8p + 8n + 8e',          time:16,   powerEV:80,       sellId:'oxygen' },
  { id:'silicon',                  label:'Silicon',                  building:'Assembler',    inputs:'14p + 14n + 14e',       time:28,   powerEV:140,      sellId:'silicon' },
  { id:'aluminum',                 label:'Aluminum',                 building:'Assembler',    inputs:'13p + 14n + 13e',       time:27,   powerEV:135,      sellId:'aluminum' },
  { id:'iron',                     label:'Iron',                     building:'Assembler',    inputs:'26p + 30n + 26e',       time:56,   powerEV:280,      sellId:'iron' },
  { id:'nickel',                   label:'Nickel',                   building:'Assembler',    inputs:'28p + 30n + 28e',       time:58,   powerEV:290,      sellId:'nickel' },
  { id:'copper',                   label:'Copper',                   building:'Assembler',    inputs:'29p + 34n + 29e',       time:63,   powerEV:315,      sellId:'copper' },
  { id:'zinc',                     label:'Zinc',                     building:'Assembler',    inputs:'30p + 35n + 30e',       time:65,   powerEV:325,      sellId:'zinc' },
  { id:'silver',                   label:'Silver',                   building:'Assembler',    inputs:'47p + 61n + 47e',       time:108,  powerEV:540,      sellId:'silver' },
  { id:'gold',                     label:'Gold',                     building:'Assembler',    inputs:'79p + 118n + 79e',      time:197,  powerEV:985,      sellId:'gold' },
  { id:'platinum',                 label:'Platinum',                 building:'Assembler',    inputs:'78p + 117n + 78e',      time:195,  powerEV:975,      sellId:'platinum' },
  { id:'tungsten',                 label:'Tungsten',                 building:'Assembler',    inputs:'74p + 110n + 74e',      time:184,  powerEV:920,      sellId:'tungsten' },
  { id:'uranium',                  label:'Uranium',                  building:'Assembler',    inputs:'92p + 146n + 92e',      time:238,  powerEV:1190,     sellId:'uranium' },
  { id:'plutonium',                label:'Plutonium',                building:'Assembler',    inputs:'94p + 150n + 94e',      time:244,  powerEV:1220,     sellId:'plutonium' },
  // Tier 2 — Isotopes (Manipulator)
  { id:'deuterium',                label:'Deuterium',                building:'Manipulator',  inputs:'H + 1n',                time:8,    powerEV:8,        sellId:'deuterium' },
  { id:'tritium',                  label:'Tritium',                  building:'Manipulator',  inputs:'H + 2n',                time:16,   powerEV:16,       sellId:'tritium' },
  { id:'carbon_14',                label:'Carbon-14',                building:'Manipulator',  inputs:'C + 2n',                time:16,   powerEV:16,       sellId:'carbon_14' },
  { id:'uranium_235',              label:'Uranium-235',              building:'Manipulator',  inputs:'U − 3n → +α',           time:24,   powerEV:24,       sellId:'uranium_235' },
  // Tier 3 — Molecules (Mol. Synth.)
  { id:'liquid_hydrogen',          label:'Liquid Hydrogen',          building:'Mol. Synth.',  inputs:'10× H',                 time:20,   powerEV:50,       sellId:'liquid_hydrogen' },
  { id:'water',                    label:'Water',                    building:'Mol. Synth.',  inputs:'4× H + 8× O',           time:60,   powerEV:200,      sellId:'water' },
  { id:'methane',                  label:'Methane',                  building:'Mol. Synth.',  inputs:'4× H + 6× C',           time:80,   powerEV:350,      sellId:'methane' },
  { id:'ammonia',                  label:'Ammonia',                  building:'Mol. Synth.',  inputs:'3× H + 7× N',           time:90,   powerEV:450,      sellId:'ammonia' },
  { id:'silica',                   label:'Silica',                   building:'Mol. Synth.',  inputs:'5× Si + 10× O',         time:150,  powerEV:800,      sellId:'silica' },
  { id:'iron_oxide',               label:'Iron Oxide',               building:'Mol. Synth.',  inputs:'4× Fe + 6× O',          time:200,  powerEV:1500,     sellId:'iron_oxide' },
  { id:'uranium_hexafluoride',     label:'UF₆',                     building:'Mol. Synth.',  inputs:'2× U + 8× Si',          time:400,  powerEV:5000,     sellId:'uranium_hexafluoride' },
  // Tier 4 — Materials (Mat. Forge)
  { id:'steel',                    label:'Steel',                    building:'Mat. Forge',   inputs:'5× Fe₂O₃ + 2× CH₄',    time:300,  powerEV:8000,     sellId:'steel' },
  { id:'carbon_fiber',             label:'Carbon Fiber',             building:'Mat. Forge',   inputs:'4× CH₄ + 3× SiO₂',     time:400,  powerEV:15000,    sellId:'carbon_fiber' },
  { id:'titanium_alloy',           label:'Titanium Alloy',           building:'Mat. Forge',   inputs:'3× SiO₂ + 4× Fe₂O₃',   time:500,  powerEV:25000,    sellId:'titanium_alloy' },
  { id:'semiconductor_wafer',      label:'Semiconductor Wafer',      building:'Mat. Forge',   inputs:'5× SiO₂ + 2× LH₂',     time:600,  powerEV:50000,    sellId:'semiconductor_wafer' },
  { id:'aerogel',                  label:'Aerogel',                  building:'Mat. Forge',   inputs:'3× SiO₂ + 2× NH₃',     time:700,  powerEV:80000,    sellId:'aerogel' },
  { id:'superconductor',           label:'Superconductor',           building:'Mat. Forge',   inputs:'2× Aerogel + 3× UF₆',  time:800,  powerEV:200000,   sellId:'superconductor' },
  { id:'metamaterial',             label:'Metamaterial',             building:'Mat. Forge',   inputs:'2× SC + 2× Si-W',       time:1000, powerEV:500000,   sellId:'metamaterial' },
  // Tier 5 — Components (Comp. Fab.)
  { id:'quantum_processor',        label:'Quantum Processor',        building:'Comp. Fab.',   inputs:'3× Si-W + 2× SC',       time:1200, powerEV:2000000,  sellId:'quantum_processor' },
  { id:'plasma_containment_ring',  label:'Plasma Containment Ring',  building:'Comp. Fab.',   inputs:'4× MM + 2× Ti-A',       time:1500, powerEV:5000000,  sellId:'plasma_containment_ring' },
  { id:'antimatter_cell',          label:'Antimatter Cell',          building:'Comp. Fab.',   inputs:'3× MM + 2× Aerogel',    time:2000, powerEV:10000000, sellId:'antimatter_cell' },
  { id:'dyson_node',               label:'Dyson Node',               building:'Comp. Fab.',   inputs:'2× QP + 2× PCR',        time:3000, powerEV:50000000, sellId:'dyson_node' },
  { id:'orbital_frame',            label:'Orbital Frame',            building:'Comp. Fab.',   inputs:'3× Ti-A + 2× DN',       time:4000, powerEV:1e8,      sellId:'orbital_frame' },
  { id:'graviton_lens',            label:'Graviton Lens',            building:'Comp. Fab.',   inputs:'2× OF + 2× AM',         time:5000, powerEV:5e8,      sellId:'graviton_lens' },
],

// ─────────────────────────────────────── BUILDINGS ──
// buf: { out: base output buffer items, in: base input buffer per slot (0 = n/a) }
// ups: speed upgrades  [{ e: cost, r: new rate }]  (output_rate multiplier; for Demon: throughput multiplier)
// sus: storage upgrades [{ e: cost, b: new max output buffer }]
buildings: [
  // Tier 1
  { id:'harvester',               label:'Harvester',               tier:1, cat:'Core',      cost:100,     draw:'—',        out:'—',     rate:'1/s',
    buf:{out:20, in:0},
    ups:[{e:500,r:'2/s'},{e:2000,r:'4/s'},{e:8000,r:'8/s'}],
    sus:[{e:300,b:150},{e:1200,b:750},{e:5000,b:4000}] },
  { id:'strong_force_combiner',   label:'Strong Force Combiner',   tier:1, cat:'Transient', cost:200,     draw:'10 eV',    out:'—',     rate:'1/s',
    buf:{out:20, in:30},
    ups:[{e:300,r:'2/s'},{e:1200,r:'4/s'},{e:5000,r:'8/s'}],
    sus:[{e:200,b:150},{e:800,b:750},{e:3500,b:4000}] },
  { id:'basic_generator',         label:'Basic Generator',         tier:1, cat:'Power',     cost:150,     draw:'—',        out:'50 eV', rate:'—',
    buf:{out:0, in:0},
    ups:[{e:400,o:'100 eV'},{e:1600,o:'200 eV'},{e:6000,o:'400 eV'}],
    sus:[] },
  { id:'maxwells_demon',          label:"Maxwell's Demon",         tier:1, cat:'Core',      cost:0,       draw:'—',        out:'—',     rate:'entropy',
    buf:{out:0, in:0},
    ups:[{e:200,r:'2× throughput'},{e:800,r:'4× throughput'}],
    sus:[{e:150,b:300},{e:600,b:1500}] },
  // Tier 2
  { id:'atomic_assembler',        label:'Atom Generator',          tier:2, cat:'Transient', cost:350,     draw:'mass×5',   out:'—',     rate:'1/s',
    buf:{out:20, in:30},
    ups:[{e:1000,r:'2/s'},{e:4000,r:'4/s'},{e:16000,r:'8/s'}],
    sus:[{e:700,b:150},{e:2800,b:750},{e:11000,b:4000}] },
  { id:'isotopic_manipulator',    label:'Isotopic Manipulator',    tier:2, cat:'Transient', cost:1500,    draw:'n×8',      out:'—',     rate:'0.5/s',
    buf:{out:15, in:30},
    ups:[{e:2000,r:'1/s'},{e:8000,r:'2/s'},{e:32000,r:'4/s'}],
    sus:[{e:1500,b:100},{e:6000,b:500},{e:24000,b:2500}] },
  { id:'radioactive_containment', label:'Radioactive Containment', tier:2, cat:'Core',      cost:2500,    draw:'30 eV',    out:'—',     rate:'decay',
    buf:{out:30, in:30},
    ups:[{e:3000,d:'50 eV'},{e:12000,d:'80 eV'}],
    sus:[{e:2000,b:200},{e:8000,b:1000}] },
  // Tier 3
  { id:'molecular_synthesizer',   label:'Molecular Synthesizer',   tier:3, cat:'Transient', cost:5000,    draw:'recipe',   out:'—',     rate:'1/s',
    buf:{out:20, in:30},
    ups:[{e:50000,r:'2/s'},{e:200000,r:'4/s'},{e:800000,r:'8/s'}],
    sus:[{e:35000,b:150},{e:140000,b:750},{e:560000,b:4000}] },
  // Tier 4
  { id:'materials_forge',         label:'Materials Forge',         tier:4, cat:'Transient', cost:50000,   draw:'recipe',   out:'—',     rate:'0.5/s',
    buf:{out:15, in:30},
    ups:[{e:1000000,r:'1/s'},{e:5000000,r:'2/s'},{e:25000000,r:'4/s'}],
    sus:[{e:700000,b:100},{e:3500000,b:500},{e:17500000,b:2500}] },
  // Tier 5
  { id:'component_fabricator',    label:'Component Fabricator',    tier:5, cat:'Transient', cost:5000000, draw:'recipe',   out:'—',     rate:'0.25/s',
    buf:{out:10, in:30},
    ups:[{e:100000000,r:'0.5/s'},{e:1000000000,r:'1/s'}],
    sus:[{e:70000000,b:75},{e:700000000,b:375}] },
],

// ─────────────────────────────────────── RESEARCH ──
research: [
  // ── Core tree (tutorial + early post-tutorial) ──
  { id:'recombination_i',        label:'Recombination I',        branch:'Nuclear',     cost:100,       discount:0.25, prereqs:'—',                                  unlocks:'Proton, Neutron, SFC' },
  { id:'hydrogen_synthesis',     label:'Hydrogen Synthesis',     branch:'Nuclear',     cost:50,        discount:0.25, prereqs:'Recombination I',                     unlocks:'Hydrogen, Atom Generator' },
  { id:'automation_i',           label:'Automation I',           branch:'Engineering', cost:25,        discount:0.50, prereqs:'Hydrogen Synthesis',                  unlocks:'Harvester' },
  { id:'nucleon_harvesting',     label:'Nucleon Harvesting',     branch:'Engineering', cost:800,       discount:0.20, prereqs:'Recombination I',                     unlocks:'Nucleon Harvester (special)' },
  { id:'rapid_extraction_i',     label:'Rapid Extraction I',     branch:'Engineering', cost:150,       discount:0.25, prereqs:'Recombination I',                     unlocks:'Field tap cooldown −15%' },
  { id:'rapid_extraction_ii',    label:'Rapid Extraction II',    branch:'Engineering', cost:600,       discount:0.20, prereqs:'Rapid Extraction I',                   unlocks:'Field tap cooldown −20% more' },
  { id:'atomic_assembly',        label:'Atomic Assembly',        branch:'Chemistry',   cost:500,       discount:0.20, prereqs:'Hydrogen Synthesis',                  unlocks:'He-4, Li, C, O, Si, Fe + gates: Light/Mid Elements, Mol. Synthesis' },
  { id:'isotopes',               label:'Isotope Engineering',    branch:'Nuclear',     cost:1500,      discount:0.15, prereqs:'Atomic Assembly',                     unlocks:'D, T, C-14, Isotopic Manipulator' },
  { id:'heavy_elements',         label:'Heavy Elements',         branch:'Nuclear',     cost:3000,      discount:0.10, prereqs:'Atomic Assembly',                     unlocks:'Uranium + gate: Transuranic Synthesis' },
  { id:'radioactive_isotopes',   label:'Radioactive Isotopes',   branch:'Nuclear',     cost:5000,      discount:0.10, prereqs:'Isotopes + Heavy Elements',           unlocks:'U-235, α/β particles, Radioactive Containment' },
  // ── Tier 2 element gates ──
  { id:'light_elements',         label:'Light Elements',         branch:'Chemistry',   cost:1000,      discount:0.20, prereqs:'Atomic Assembly',                     unlocks:'Beryllium, Boron' },
  { id:'mid_elements',           label:'Mid-Period Elements',    branch:'Chemistry',   cost:2000,      discount:0.20, prereqs:'Atomic Assembly',                     unlocks:'Nitrogen, Aluminum, Nickel' },
  { id:'transition_metals',      label:'Transition Metals',      branch:'Materials',   cost:8000,      discount:0.15, prereqs:'Mid-Period Elements',                 unlocks:'Copper, Zinc' },
  { id:'precious_metals',        label:'Precious Metals',        branch:'Materials',   cost:25000,     discount:0.10, prereqs:'Transition Metals',                   unlocks:'Silver, Gold' },
  { id:'exotic_metals',          label:'Exotic Metals',          branch:'Materials',   cost:80000,     discount:0.10, prereqs:'Precious Metals',                     unlocks:'Platinum, Tungsten' },
  { id:'transuranic_elements',   label:'Transuranic Synthesis',  branch:'Nuclear',     cost:500000,    discount:0.05, prereqs:'Heavy Elements + Exotic Metals',      unlocks:'Plutonium' },
  // ── Tier 3–5 processing gates ──
  { id:'molecular_synthesis',    label:'Molecular Synthesis',    branch:'Chemistry',   cost:3000,      discount:0.15, prereqs:'Atomic Assembly',                     unlocks:'LH₂, H₂O, CH₄, NH₃, Molecular Synthesizer' },
  { id:'advanced_molecules',     label:'Advanced Molecules',     branch:'Chemistry',   cost:20000,     discount:0.10, prereqs:'Mol. Synthesis + Mid Elements',       unlocks:'SiO₂, Fe₂O₃, UF₆' },
  { id:'materials_science',      label:'Materials Science',      branch:'Materials',   cost:150000,    discount:0.10, prereqs:'Adv. Molecules + Transition Metals',  unlocks:'Steel, Carbon Fiber, Ti Alloy, Materials Forge + Quantum Domains intro' },
  { id:'advanced_materials',     label:'Advanced Materials',     branch:'Materials',   cost:800000,    discount:0.05, prereqs:'Materials Science + Precious Metals', unlocks:'Si Wafer, Aerogel, Superconductor, Metamaterial' },
  { id:'component_engineering',  label:'Component Engineering',  branch:'Engineering', cost:5000000,   discount:0.05, prereqs:'Adv. Materials + Transuranic',        unlocks:'Quantum Proc., Plasma Ring, Antimatter Cell, Comp. Fabricator' },
  { id:'megastructure_theory',   label:'Megastructure Theory',   branch:'Astrophysics',cost:50000000,  discount:0.05, prereqs:'Component Engineering',               unlocks:'Dyson Node, Orbital Frame, Graviton Lens' },
],

// ─────────────────────────────────────── TUTORIAL PHASES ──
// row_cls: 'dialogue'|'gate'|'milestone'|''
// tag_type: 'dialogue'|'hint'|'gate'|'milestone'|'silent'
// cost/reward: { t: CSS class ('neutral'|'cost'|'reward'), v: text (\n = <br>), bold: true? }
tutorial_phases: [
  {
    phase: 1, label: '① Learning the Loop',
    color: '#1a5c8a', bg: '#0f1f30',
    meta: 'Steps 0–4 · Tutorial teaches: what fields are, what Maxwell\'s Demon does, what entropy is',
    entropy_note: { text: 'Entropy after phase: ~5', cls: 'reward' },
    steps: [
      { num:0, id:'intro_dialogue',             row_cls:'dialogue',  tag_type:'dialogue',
        action:'Read Architect intro. Describes the Quantum Foundry, particles, the Lepton Field.',
        dlg_ref:'intro_architect · 4 lines · game paused',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Context'},
        time:'~2 min', advance:'dialogue_complete' },
      { num:1, id:'collect_first_electron',     row_cls:'',          tag_type:'hint',
        action:'Tap the Lepton Field to collect electrons. Only lepton field unlocked; quark field + buildings blocked.',
        dlg_ref:'first_electron_intro · 1 line · game live',
        cost:{t:'neutral',v:'Taps'}, reward:{t:'reward',v:'5 Electrons'},
        time:'~15 sec', advance:'inventory ≥ 5e' },
      { num:2, id:'direct_to_maxwells_demon',   row_cls:'dialogue',  tag_type:'hint',
        action:"Camera pans + highlight on Maxwell's Demon. Open it by tapping.",
        dlg_ref:'demon_intro · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Opens Demon'},
        time:'~15 sec', advance:'demon_opened' },
      { num:3, id:'sell_electrons_in_demon',    row_cls:'dialogue',  tag_type:'hint',
        action:'Drag electrons to the deposit side. Electron row highlighted amber inside panel.',
        dlg_ref:'sell_electrons_intro · 1 line · game live',
        cost:{t:'cost',v:'−5 Electrons'}, reward:{t:'reward',v:'+5 entropy'},
        time:'~30 sec', advance:'inventory(e) = 0' },
      { num:4, id:'close_demon_panel',          row_cls:'dialogue',  tag_type:'hint',
        action:"Close Maxwell's Demon panel.",
        dlg_ref:'close_demon_first · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'—'},
        time:'~5 sec', advance:'demon_closed' },
    ]
  },
  {
    phase: 2, label: '② Quarks',
    color: '#4a5568', bg: '#1a1d21',
    meta: 'Steps 5–9 · Tutorial teaches: quark fields, Up vs Down Quarks as crafting ingredients',
    entropy_note: { text: 'Entropy after phase: ~15', cls: 'reward' },
    steps: [
      { num:5, id:'explain_quark_field',               row_cls:'dialogue', tag_type:'dialogue',
        action:'Architect introduces the Quark Field and the entropy it just generated.',
        dlg_ref:'quark_field_explanation · 2 lines · game paused',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Context'},
        time:'~1 min', advance:'dialogue_complete' },
      { num:6, id:'collect_quarks',                    row_cls:'',         tag_type:'silent',
        action:'Tap the Quark Field to collect 5 Up Quarks and 5 Down Quarks. Lepton field + buildings blocked.',
        cost:{t:'neutral',v:'Taps'}, reward:{t:'reward',v:'5× Up Quark\n5× Down Quark'},
        time:'~30 sec', advance:'inventory ≥ 5↑, 5↓' },
      { num:7, id:'direct_to_maxwells_demon_quarks',   row_cls:'dialogue', tag_type:'hint',
        action:"Highlight on Maxwell's Demon. Open it.",
        dlg_ref:'demon_quarks_intro · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Opens Demon'},
        time:'~15 sec', advance:'demon_opened' },
      { num:8, id:'sell_quarks_in_demon',              row_cls:'dialogue', tag_type:'hint',
        action:'Drag all quarks to deposit side. Both quark rows highlighted amber.',
        dlg_ref:'sell_quarks_intro · 1 line · game live',
        cost:{t:'cost',v:'−5 Up Quark\n−5 Down Quark'}, reward:{t:'reward',v:'+10 entropy'},
        time:'~30 sec', advance:'inventory(↑↓) = 0' },
      { num:9, id:'close_demon_panel_quarks',          row_cls:'dialogue', tag_type:'hint',
        action:"Close Maxwell's Demon panel.",
        dlg_ref:'close_demon_quarks · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'—'},
        time:'~5 sec', advance:'demon_closed' },
    ]
  },
  {
    phase: 3, label: '③ Recombination I',
    color: '#8a4a00', bg: '#2b1d0e',
    meta: 'Steps 10–12 · First research gate · teaches: research panel, strong nuclear force',
    entropy_note: { text: 'GATE: 100 entropy (player has ~265 with 250 starting + sells)', cls: 'reward' },
    steps: [
      { num:10, id:'open_research_drawer',    row_cls:'dialogue', tag_type:'hint',
        action:'Research drawer handle pulses. Open the left-side menu to reveal the Research panel.',
        dlg_ref:'research_panel_intro · 1 line · game paused',
        warning:'Collection blocked. Player cannot farm more entropy here.',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Reveals Research panel'},
        time:'~10 sec', advance:'drawer_opened' },
      { num:11, id:'buy_recombination_i',     row_cls:'gate',     tag_type:'gate',
        action:'Research button pulses. Buy Recombination I.',
        dlg_ref:'recombination_i_context · 1 line · game paused',
        note:'✅ Resolved: player starts with 250 entropy; tutorial earns +15 more (265 total) before this gate.',
        cost:{t:'cost',v:'−100 entropy',bold:true}, reward:{t:'reward',v:'Unlocks: Strong Force Combiner, Proton recipe, Neutron recipe'},
        time:'~30 sec', advance:'recombination_i unlocked' },
      { num:12, id:'recombination_unlocked',  row_cls:'dialogue', tag_type:'dialogue',
        action:'Architect explains the strong nuclear force, proton structure (2U+1D), and neutron structure (1U+2D).',
        dlg_ref:'recombination_unlocked_dialogue · 2 lines · game paused',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Context'},
        time:'~1 min', advance:'dialogue_complete' },
    ]
  },
  {
    phase: 4, label: '④ Nucleons',
    color: '#4a5568', bg: '#1a1d21',
    meta: 'Steps 13–19 · Tutorial teaches: recipe panel, manual crafting, value ladder (quarks → nucleons)',
    entropy_note: { text: 'Entropy gained this phase: +12 (2P×3 + 2N×3)', cls: 'reward' },
    steps: [
      { num:13, id:'collect_quarks_for_nucleons',       row_cls:'',         tag_type:'silent',
        action:'Tap Quark Field to collect 8 Up + 8 Down Quarks (enough for 2P + 2N). Hydrogen Synthesis research hidden.',
        cost:{t:'neutral',v:'Taps'}, reward:{t:'reward',v:'8× Up + 8× Down'},
        time:'~45 sec', advance:'inventory ≥ 8↑, 8↓' },
      { num:14, id:'open_recipe_panel_for_nucleons',    row_cls:'dialogue', tag_type:'hint',
        action:'Recipe button pulses. Open the recipe panel.',
        dlg_ref:'open_recipe_panel_context · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Reveals recipe panel'},
        time:'~15 sec', advance:'recipe_panel_opened' },
      { num:15, id:'craft_two_protons',                 row_cls:'dialogue', tag_type:'hint',
        action:'Craft 2 Protons. Recipe: 2× Up Quark + 1× Down Quark each. Craft time: 1.0s each.',
        dlg_ref:'craft_protons_context · 1 line · game paused',
        cost:{t:'cost',v:'−4× Up Quark\n−2× Down Quark'}, reward:{t:'reward',v:'2× Proton'},
        time:'~30 sec', advance:'inventory ≥ 2P' },
      { num:16, id:'craft_two_neutrons',                row_cls:'dialogue', tag_type:'hint',
        action:'Craft 2 Neutrons. Recipe: 1× Up Quark + 2× Down Quark each. Craft time: 1.0s each.',
        dlg_ref:'craft_neutrons_context · 1 line · game paused',
        cost:{t:'cost',v:'−2× Up Quark\n−4× Down Quark'}, reward:{t:'reward',v:'2× Neutron'},
        time:'~30 sec', advance:'inventory ≥ 2N' },
      { num:17, id:'direct_to_demon_for_nucleons',      row_cls:'dialogue', tag_type:'hint',
        action:"Highlight on Maxwell's Demon. Open it.",
        dlg_ref:'nucleons_to_demon_context · 1 line · game paused',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Opens Demon'},
        time:'~15 sec', advance:'demon_opened' },
      { num:18, id:'sell_protons_and_neutrons',         row_cls:'dialogue', tag_type:'hint',
        action:'Drag Protons and Neutrons to deposit side. Both rows highlighted amber.',
        dlg_ref:'sell_nucleons_context · 1 line · game live',
        cost:{t:'cost',v:'−2× Proton\n−2× Neutron'}, reward:{t:'reward',v:'+12 entropy',sub:'3× each = 6+6'},
        time:'~30 sec', advance:'inventory(P+N) = 0' },
      { num:19, id:'close_demon_after_nucleons',        row_cls:'dialogue', tag_type:'hint',
        action:"Close Maxwell's Demon panel.",
        dlg_ref:'close_demon_nucleons_context · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'—'},
        time:'~5 sec', advance:'demon_closed' },
    ]
  },
  {
    phase: 5, label: '⑤ Hydrogen Synthesis',
    color: '#4a5568', bg: '#1a1d21',
    meta: 'Steps 20–27 · Second research gate · teaches: combining nucleons + electrons, value ladder compounds',
    entropy_note: { text: 'GATE: 50 entropy · H earned this phase: +10', cls: '', color: '#f0883e' },
    steps: [
      { num:20, id:'buy_hydrogen_synthesis',         row_cls:'gate',     tag_type:'gate',
        action:'Research button pulses. Buy Hydrogen Synthesis.',
        dlg_ref:'unlock_hydrogen_synthesis · 1 line · game paused · triggers on enter',
        cost:{t:'cost',v:'−50 entropy',bold:true}, reward:{t:'reward',v:'Unlocks: Hydrogen recipe (1P + 1e = H, 2.0s, 5 eV power)'},
        time:'~30 sec', advance:'hydrogen_synthesis unlocked' },
      { num:21, id:'gather_for_hydrogen',            row_cls:'dialogue', tag_type:'hint',
        action:'Collect resources: 4× Up Quark, 2× Down Quark, 2× Electron. Buildings still blocked.',
        dlg_ref:'gather_hydrogen_context · 2 lines · game paused → live',
        cost:{t:'neutral',v:'Taps'}, reward:{t:'reward',v:'4↑ + 2↓ + 2e'},
        time:'~30 sec', advance:'inventory ≥ 4↑, 2↓, 2e' },
      { num:22, id:'craft_protons_for_hydrogen',     row_cls:'dialogue', tag_type:'hint',
        action:'Craft 2 Protons (2U+1D each, 1.0s each). Recipe button pulses.',
        dlg_ref:'craft_protons_for_h_context · 1 line · game live',
        cost:{t:'cost',v:'−4× Up Quark\n−2× Down Quark'}, reward:{t:'reward',v:'2× Proton'},
        time:'~20 sec', advance:'inventory ≥ 2P' },
      { num:23, id:'craft_hydrogen',                 row_cls:'dialogue', tag_type:'hint',
        action:'Craft 2 Hydrogen atoms (1P + 1e each, 2.0s each, 5 eV power). Recipe button pulses.',
        dlg_ref:'craft_hydrogen_context · 1 line · game paused',
        cost:{t:'cost',v:'−2× Proton\n−2× Electron'}, reward:{t:'reward',v:'2× Hydrogen'},
        time:'~30 sec', advance:'inventory ≥ 2H' },
      { num:24, id:'hydrogen_crafted',               row_cls:'milestone', tag_type:'milestone',
        action:'Architect celebrates. First complete atom built.',
        dlg_ref:'hydrogen_crafted_dialogue · 1 line · game paused',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Narrative milestone'},
        time:'~30 sec', advance:'dialogue_complete' },
      { num:25, id:'direct_to_demon_for_hydrogen',   row_cls:'',         tag_type:'silent',
        action:"Camera pans + full highlight on Maxwell's Demon. Open it.",
        dlg_ref:'hydrogen_to_demon · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Opens Demon'},
        time:'~15 sec', advance:'demon_opened' },
      { num:26, id:'sell_hydrogen_in_demon',         row_cls:'',         tag_type:'silent',
        action:'Deposit 2 Hydrogen atoms. Hydrogen row highlighted amber.',
        cost:{t:'cost',v:'−2× Hydrogen'}, reward:{t:'reward',v:'+10 entropy',sub:'5× each'},
        time:'~20 sec', advance:'inventory(H) = 0' },
      { num:27, id:'close_demon_after_hydrogen',     row_cls:'',         tag_type:'silent',
        action:"Close Maxwell's Demon panel.",
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'—'},
        time:'~5 sec', advance:'demon_closed' },
    ]
  },
  {
    phase: 6, label: '⑥ Automation',
    color: '#1a6e40', bg: '#0d2018',
    meta: 'Steps 28–50 · Tutorial teaches: Automation I research, Harvester placement, conveyor routing, power system, SFC dual-recipe, nucleon pipeline, Atom Generator, hydrogen assembly, building upgrades · <strong style="color:#3fb950">Tutorial ends at step 50</strong>',
    entropy_note: { text: 'Tutorial end state: H loop ~4e/sec · Speed Upgrade L2 purchased on Atom Generator (2× throughput) · ~8e/sec after upgrade', cls: '', color: '#3fb950' },
    steps: [
      { num:28, id:'intro_automation_i',         row_cls:'dialogue', tag_type:'dialogue',
        action:'Architect explains what Automation I unlocks and the Harvester concept.',
        dlg_ref:'intro_automation_i · 3 lines · game paused (×2), then live',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Context'},
        time:'~90 sec', advance:'dialogue_complete' },
      { num:29, id:'buy_automation_i',           row_cls:'gate',     tag_type:'gate',
        action:'Research button pulses. Buy Automation I.',
        dlg_ref:'buy_automation_i_context · 1 line · game live',
        note:'✅ Resolved (gap-3): Automation I is now a real research node. Cost: 25e. Discount on prestige: 50%.',
        cost:{t:'cost',v:'−25 entropy',bold:true}, reward:{t:'reward',v:'Unlocks: Harvester building'},
        time:'~20 sec', advance:'automation_i unlocked' },
      { num:30, id:'place_first_building',       row_cls:'',         tag_type:'hint',
        action:'Build button pulses. Open Build menu. Place a Harvester on a field tile.',
        dlg_ref:'place_building_context · 1 line · game live',
        cost:{t:'cost',v:'−100 entropy\n(Harvester · entropy_cost)'}, reward:{t:'reward',v:'Automated collection begins'},
        time:'~1 min', advance:'building_count ≥ 2' },
      { num:31, id:'place_conveyor',             row_cls:'',         tag_type:'hint',
        action:"Conveyor button pulses. Connect Harvester output → Maxwell's Demon input with a conveyor.",
        dlg_ref:'place_conveyor_context · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'Particles flow automatically to Demon'},
        time:'~30 sec', advance:'conveyor_placed' },
      { num:32, id:'automation_started',         row_cls:'dialogue', tag_type:'dialogue',
        action:'Architect acknowledges idle loop running. Teases quark-to-nucleon upgrade as next goal.',
        dlg_ref:'automation_context · 2 lines · game paused → live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'Idle loop active'},
        time:'~1 min', advance:'dialogue_complete' },
      { type:'subheader', text:'⑥ cont. — Nucleon Automation Pipeline' },
      { num:33, id:'intro_sfc',                  row_cls:'dialogue', tag_type:'dialogue',
        action:'Silent wait until entropy ≥ 200. Dialogue fires on enter: Architect directs player to place the Strong Force Combiner.',
        dlg_ref:'intro_sfc_context · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'SFC unlocked (Recomb I already purchased)'},
        time:'~3 min idle', advance:'entropy ≥ 200' },
      { num:34, id:'place_sfc',                  row_cls:'',         tag_type:'hint',
        action:'Build menu pulses. Place a Strong Force Combiner. Shows "Unpowered" state immediately.',
        dlg_ref:'place_sfc_context · 1 line · game live',
        cost:{t:'cost',v:'−200 entropy'}, reward:{t:'reward',v:'SFC placed · power system introduced'},
        time:'~30 sec', advance:'sfc_count ≥ 1' },
      { num:35, id:'place_generator',            row_cls:'dialogue', tag_type:'hint',
        action:'Architect introduces power radius. Generator button pulses. Place a Basic Generator within 3 tiles of the SFC.',
        dlg_ref:'place_generator_context · 1 line · game live',
        cost:{t:'cost',v:'−150 entropy'}, reward:{t:'reward',v:'Generator placed · power radius visualised'},
        time:'~2 min idle + 30 sec', advance:'generator_count ≥ 1' },
      { num:36, id:'link_generator',             row_cls:'dialogue', tag_type:'hint',
        action:'Select SFC → drag power link to Generator. SFC powers on.',
        dlg_ref:'link_generator_context · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'SFC online · 10 eV drawn from 50 eV supply'},
        time:'~30 sec', advance:'power_link_established' },
      { num:37, id:'place_quark_harvester',      row_cls:'',         tag_type:'hint',
        action:'Place a second Harvester on the quark field. Produces Up Quarks + Down Quarks at 1:1 ratio.',
        dlg_ref:'place_quark_harvester_context · 1 line · game live',
        cost:{t:'cost',v:'−100 entropy'}, reward:{t:'reward',v:'Quark supply running'},
        time:'~1 min idle + 30 sec', advance:'harvester_count ≥ 2' },
      { num:38, id:'route_quarks_to_sfc',        row_cls:'',         tag_type:'hint',
        action:'Connect quark Harvester output → SFC input port with a conveyor.',
        dlg_ref:'route_quarks_context · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'Quarks flowing into SFC'},
        time:'~30 sec', advance:'conveyor_placed' },
      { num:39, id:'configure_sfc_recipes',      row_cls:'dialogue', tag_type:'dialogue',
        action:'SFC recipe panel opens. Architect explains dual-recipe: enable both Proton (2U+1D) and Neutron (1U+2D) simultaneously — self-balancing with 1:1 quark input.',
        dlg_ref:'configure_sfc_context · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'Both recipes active · balanced quark usage'},
        time:'~30 sec', advance:'combiner_both_recipes_enabled' },
      { num:40, id:'route_nucleons_to_demon',    row_cls:'',         tag_type:'hint',
        action:"Two output ports on SFC (proton + neutron). Connect both to Maxwell's Demon with two conveyors.",
        dlg_ref:'route_nucleons_context · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'Protons (3e) + Neutrons (3e) selling automatically'},
        time:'~1 min', advance:'sfc_outputs_connected' },
      { num:41, id:'nucleon_loop_complete',      row_cls:'dialogue', tag_type:'dialogue',
        action:'Architect acknowledges two-stream factory. Directs player toward the Atom Generator.',
        dlg_ref:'nucleon_loop_context · 2 lines · game paused → live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'Nucleon pipeline confirmed · Atom Generator teased'},
        time:'~1 min', advance:'dialogue_complete' },
      { type:'subheader', text:'⑥ cont. — Hydrogen Automation' },
      { num:42, id:'intro_atom_gen',             row_cls:'dialogue', tag_type:'dialogue',
        action:'Silent wait until entropy ≥ 350. Dialogue fires on enter: Architect directs player to place the Atom Generator.',
        dlg_ref:'intro_atom_gen_context · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'Atom Generator available (research already done)'},
        time:'~2–4 min idle', advance:'entropy ≥ 350' },
      { num:43, id:'place_atom_generator',       row_cls:'',         tag_type:'hint',
        action:'Build menu pulses. Place the Atom Generator. Uses 5 eV — existing generator handles it alongside the SFC (15 eV total vs 50 eV supply).',
        dlg_ref:'place_atom_gen_context · 1 line · game live',
        cost:{t:'cost',v:'−350 entropy'}, reward:{t:'reward',v:'Atom Generator placed'},
        time:'~30 sec', advance:'atom_gen_count ≥ 1' },
      { num:44, id:'delete_reroute_conveyors',   row_cls:'dialogue', tag_type:'dialogue',
        action:'Architect teaches conveyor deletion before asking the player to reroute. Tap any conveyor to select it — a trash icon appears on the belt. Player must delete the proton conveyor (SFC → Demon) and the electron conveyor (Harvester → Demon) to free those ports.',
        dlg_ref:'delete_conveyor_context · 2 lines · game live · first line teaches mechanic, second prompts action',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'Teaches conveyor deletion · proton + electron ports freed'},
        time:'~30 sec', advance:'conveyors_deleted (proton+electron to Demon removed)' },
      { num:45, id:'connect_atom_gen_inputs',    row_cls:'dialogue', tag_type:'dialogue',
        action:'Architect explains rerouting: proton conveyor from SFC → Atom Generator; electron Harvester output → Atom Generator. Neutron output stays at Demon.',
        dlg_ref:'connect_atom_gen_context · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'Both input ports connected · rerouting complete'},
        time:'~1 min', advance:'atom_gen_inputs_connected' },
      { num:46, id:'route_hydrogen_to_demon',    row_cls:'',         tag_type:'hint',
        action:"Connect Atom Generator hydrogen output → Maxwell's Demon. Hydrogen sells for 5e.",
        dlg_ref:'route_hydrogen_context · 1 line · game live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'Hydrogen (5e) selling automatically'},
        time:'~30 sec', advance:'atom_gen_output_connected' },
      { num:47, id:'hydrogen_loop_complete',     row_cls:'dialogue', tag_type:'dialogue',
        action:'Architect names the first atom, teases the full periodic table. Upgrade tutorial begins next.',
        dlg_ref:'hydrogen_loop_context · 2 lines · game paused → live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'H loop confirmed · ~4e/sec · upgrade phase starts'},
        time:'~1 min', advance:'dialogue_complete' },
      { type:'subheader', text:'⑥ cont. — Building Upgrades' },
      { num:48, id:'intro_building_upgrades',    row_cls:'dialogue', tag_type:'dialogue',
        action:'Architect introduces Speed and Storage upgrade tracks. Atom Generator highlighted (VisualOnly). Ends with prompt to tap the Atom Generator.',
        dlg_ref:'intro_upgrades_context · 4 lines · game paused → live → live → live (highlight: atomic_assembler)',
        cost:{t:'neutral',v:'—'}, reward:{t:'neutral',v:'Player understands both upgrade tracks'},
        time:'~90 sec', advance:'dialogue_complete' },
      { num:49, id:'upgrade_atom_generator_speed', row_cls:'gate', tag_type:'gate',
        action:'Camera pans full to Atom Generator. <code>AtomicAssemblerOnly</code> gate: only Atom Generator can be opened. Player taps it and buys Speed Upgrade L2.',
        note:'Advance fires via TutorialOverlayController.NotifyAtomGeneratorSpeedUpgraded() — call from building upgrade UI when building_type == 4.',
        cost:{t:'cost',v:'−1,000 entropy',bold:true}, reward:{t:'reward',v:'Atom Generator 2× speed · H income doubles to ~8e/sec'},
        time:'~2–5 min idle', advance:'atom_generator_speed_upgraded' },
      { num:50, id:'upgrade_context',            row_cls:'milestone', tag_type:'milestone',
        action:'Architect confirms 2× throughput. Explains Storage track for offline running. Notes prestige discount on upgrades. <strong style="color:#3fb950">Tutorial ends when dialogue completes.</strong>',
        dlg_ref:'upgrade_complete_context · 3 lines · game paused → live → live',
        cost:{t:'neutral',v:'—'}, reward:{t:'reward',v:'Tutorial complete · both upgrade tracks introduced · ~8e/sec'},
        time:'~90 sec', advance:'dialogue_complete' },
    ]
  },
],

// ─────────────────────────────────────── POST-TUTORIAL PHASES ──
post_tutorial_phases: [
  {
    phase: 7, label: '⑦ Run 2 — Express Ramp-Up',
    color: '#3a2c5c', bg: '#1a1228',
    meta: 'Re-unlock tree at 25–50% discount · SFC + generator already known from tutorial',
    entropy_note: { text: 'Prestige discounts: Recombination I → 75e · Hydrogen Synthesis → 38e · Automation I → 13e · SFC pipeline ~338e total', color: '#d2a8ff' },
    col_last: 'Unlock Condition',
    steps: [
      { id:'Re-unlock research tree',
        action:'Re-buy Recombination I, Hydrogen Synthesis, Automation I at discounted rates. Place Harvester + conveyor immediately.',
        cost:{t:'cost',v:'~126e total'}, reward:{t:'reward',v:'Back to idle production in ~5 min'},
        time:'~5 min', advance:'prestige discount' },
      { id:'Rebuild SFC pipeline', tag_type:'building',
        action:'Re-place Strong Force Combiner + Basic Generator + quark Harvester. Player knows the layout from tutorial — much faster rebuild. Both proton + neutron recipes re-enabled.',
        cost:{t:'cost',v:'~338e (discounted)'}, reward:{t:'reward',v:'Full nucleon pipeline restored in ~5 min'},
        time:'~5 min', advance:'recombination_i unlocked' },
    ]
  },
  {
    phase: 8, label: '⑧ Atomic Assembly',
    color: '#3a4a2c', bg: '#1a2012',
    meta: 'First-time unlock · teaches: power system, multi-input buildings, value ladder compounds',
    entropy_note: { text: 'GATE: 500e (375e at 20% repeat discount) · Sell values: He-4 ×10, Li ×20, C ×160, O ×640, Si ×1,280, Fe ×5,120', color: '#3fb950' },
    col_last: 'Unlock Condition',
    steps: [
      { id:'Unlock Atomic Assembly', tag_type:'gate',
        action:'Buy Atomic Assembly research. Unlocks Atomic Assembler building and recipes for Helium-4 through Carbon.',
        cost:{t:'cost',v:'−500e (375e repeat)',bold:true}, reward:{t:'reward',v:'Atomic Assembler · He-4 · Li · C · O · Si · Fe recipes'},
        time:'~10 min idle', advance:'atomic_assembly unlocked' },
      { id:'Build Atomic Assembler', tag_type:'building',
        action:'Place Atomic Assembler. Requires power (atomic_mass × 5 EV per craft). Wire 3 input conveyors: Protons + Neutrons + Electrons.',
        cost:{t:'cost',v:'800e + generator(s)'}, reward:{t:'reward',v:'Heavy element production'},
        time:'~2 min', advance:'atomic_assembly unlocked' },
      { id:'Produce heavy elements',
        action:'Assembler crafts Helium-4 (2P+2N+2e), Lithium, Carbon, Oxygen, Silicon, Iron. Each sells for dramatically more entropy than hydrogen.',
        cost:{t:'neutral',v:'Quarks + power'}, reward:{t:'reward',v:'Fe = 5,120e · Prestige wall approaches fast'},
        time:'Ongoing', advance:'automated' },
    ]
  },
  {
    phase: 9, label: '⑨ Heavy Elements & Uranium',
    color: '#4a3a1c', bg: '#231a0a',
    meta: 'Unlock Heavy Elements research · teaches: power scaling, late-game entropy density',
    entropy_note: { text: 'GATE: 3000e · Uranium = 1,310,720e per unit', color: '#f0883e' },
    col_last: 'Unlock Condition',
    steps: [
      { id:'Unlock Heavy Elements', tag_type:'gate',
        action:'Buy Heavy Elements research (branch of Atomic Assembly). Unlocks Uranium recipe in the Assembler.',
        cost:{t:'cost',v:'−3000e',bold:true}, reward:{t:'reward',v:'Uranium recipe (92P + 146N + 92e) · 1,310,720e sell value'},
        time:'~30 min idle', advance:'heavy_elements unlocked' },
      { id:'Scale power infrastructure',
        action:'Uranium requires 238 × 5 = 1,190 eV per craft. A fully upgraded Basic Generator outputs 400 eV — multiple generators needed. Add or upgrade generators before attempting synthesis.',
        cost:{t:'cost',v:'Generators + entropy'}, reward:{t:'reward',v:'Uranium production → approaching prestige wall (50,000e net worth)'},
        time:'~5 min', advance:'heavy_elements unlocked' },
    ]
  },
  {
    phase: 10, label: '⑩ Isotopes',
    color: '#2c3a4a', bg: '#0d1a28',
    meta: 'Unlock Isotope Engineering · teaches: neutron manipulation, isotope multiplier',
    entropy_note: { text: 'GATE: 1500e · Isotopes sell at 1.5× base element value', color: '#58a6ff' },
    col_last: 'Unlock Condition',
    steps: [
      { id:'Unlock Isotope Engineering', tag_type:'gate',
        action:'Buy Isotope Engineering. Unlocks Isotopic Manipulator building.',
        cost:{t:'cost',v:'−1500e',bold:true}, reward:{t:'reward',v:'Isotopic Manipulator · Deuterium · Tritium · C-14 recipes'},
        time:'—', advance:'isotopes unlocked' },
      { id:'Build Isotopic Manipulator', tag_type:'building',
        action:'Place Manipulator. Input: Base Element + Neutrons. Dynamic power cost (neutrons adjusted × 8 EV). Output: Isotope at 1.5× sell value.',
        cost:{t:'cost',v:'1500e'}, reward:{t:'reward',v:'Deuterium, Tritium, Carbon-14'},
        time:'~2 min', advance:'isotopes unlocked' },
    ]
  },
  {
    phase: 11, label: '⑪ Radioactive Decay',
    color: '#4a2c2c', bg: '#2a0f0f',
    meta: 'Unlock Radioactive Isotopes · teaches: passive decay collection, alpha/beta particles as secondary income',
    entropy_note: { text: 'GATE: 5000e · Requires both Isotopes + Heavy Elements', color: '#f85149' },
    col_last: 'Unlock Condition',
    steps: [
      { id:'Unlock Radioactive Isotopes', tag_type:'gate',
        action:'Buy Radioactive Isotopes. Requires both Isotope Engineering + Heavy Elements. Unlocks Radioactive Containment building and U-235 recipe.',
        cost:{t:'cost',v:'−5000e',bold:true}, reward:{t:'reward',v:'Radioactive Containment · U-235 · Alpha + Beta particles'},
        time:'—', advance:'radioactive_isotopes unlocked' },
      { id:'Build Radioactive Containment', tag_type:'building',
        action:'2×2 footprint. Houses isotope-producing buildings. Passively harvests decay particles: Alpha (20 EV) + Beta (10 EV) per decay event. Requires 30 EV power.',
        cost:{t:'cost',v:'2500e'}, reward:{t:'reward',v:'Passive alpha/beta particle income stream'},
        time:'~3 min', advance:'radioactive_isotopes unlocked' },
    ]
  },
  {
    phase: 12, label: '⑫ Elements Expansion + Molecular Synthesis',
    color: '#2c4a5c', bg: '#0d1e2b',
    meta: 'Unlock the full 20-element periodic ladder and Tier 3 molecules · teaches: Molecular Synthesizer, compound value multiplier',
    entropy_note: { text: 'Element gates: light_elements 1K → mid 2K → transition 8K → precious 25K → exotic 80K · Mol. Synthesis gate: 3K', color: '#45B7D1' },
    col_last: 'Unlock Condition',
    steps: [
      { id:'Unlock Light/Mid Elements', tag_type:'gate',
        action:'Buy Light Elements (1000e) and Mid-Period Elements (2000e). Unlocks Be, B, N, Al, Ni in the Atom Generator.',
        cost:{t:'cost',v:'−3000e total',bold:true}, reward:{t:'reward',v:'Be ×40, B ×80, N ×320, Al ×2560, Ni ×10,240'},
        time:'~1 prestige run', advance:'light_elements + mid_elements unlocked' },
      { id:'Unlock Transition/Precious/Exotic Metals', tag_type:'gate',
        action:'Chain through Transition Metals (8K), Precious Metals (25K), Exotic Metals (80K). Each gate unlocks 2 elements of escalating value.',
        cost:{t:'cost',v:'−113,000e total',bold:true}, reward:{t:'reward',v:'Cu ×20K, Zn ×41K, Ag ×82K, Au ×164K, Pt ×328K, W ×655K'},
        time:'~2–3 prestige runs', advance:'exotic_metals unlocked' },
      { id:'Unlock Molecular Synthesis', tag_type:'gate',
        action:'Buy Molecular Synthesis (3000e). Unlocks Molecular Synthesizer building and first 4 molecule recipes.',
        cost:{t:'cost',v:'−3000e',bold:true}, reward:{t:'reward',v:'Molecular Synthesizer · LH₂ · H₂O · CH₄ · NH₃'},
        time:'~1 prestige run', advance:'molecular_synthesis unlocked' },
      { id:'Build Molecular Synthesizer', tag_type:'building',
        action:'Place Molecular Synthesizer. 2 input slots (primary + secondary element). Fixed power cost per recipe. Starts at 1/s output.',
        cost:{t:'cost',v:'5000e'}, reward:{t:'reward',v:'First Tier 3 production — LH₂ at 300e/unit'},
        time:'~2 min', advance:'molecular_synthesis unlocked' },
      { id:'Unlock Advanced Molecules', tag_type:'gate',
        action:'Buy Advanced Molecules (20K). Requires both Molecular Synthesis + Mid-Period Elements. Unlocks SiO₂, Fe₂O₃, UF₆.',
        cost:{t:'cost',v:'−20,000e',bold:true}, reward:{t:'reward',v:'Silica ×50K · Iron Oxide ×200K · UF₆ ×8M'},
        time:'~1 prestige run', advance:'advanced_molecules unlocked' },
    ]
  },
  {
    phase: 13, label: '⑬ Materials Science',
    color: '#2c5c3a', bg: '#0d2b15',
    meta: 'Unlock Tier 4 alloys and advanced materials · teaches: Materials Forge, multi-molecule inputs, very long craft times',
    entropy_note: { text: 'GATE: 150K (materials_science) · Steel = 1.5M · Carbon Fiber = 6M · Ti Alloy = 25M', color: '#96CEB4' },
    col_last: 'Unlock Condition',
    steps: [
      { id:'Unlock Materials Science', tag_type:'gate',
        action:'Buy Materials Science (150K). Requires Advanced Molecules + Transition Metals. Unlocks Materials Forge and Steel, Carbon Fiber, Titanium Alloy recipes.',
        cost:{t:'cost',v:'−150,000e',bold:true}, reward:{t:'reward',v:'Materials Forge · Steel · Carbon Fiber · Ti Alloy'},
        time:'~3–4 prestige runs', advance:'materials_science unlocked' },
      { id:'Quantum Domains unlocked', tag_type:'milestone',
        action:'Buying Materials Science triggers the one-shot Quantum Domains intro. A second build site — Quark Sea (quark-dense, no electron fields) — can be anchored for 250K entropy. Exactly one site is live; inactive sites keep producing offline. The unlock survives prestige (the grids do not).',
        cost:{t:'cost',v:'−250,000e (Quark Sea)'}, reward:{t:'reward',v:'2nd build site · per-site grid + field distribution · offline income across sites'},
        time:'—', advance:'materials_science unlocked' },
      { id:'Build Materials Forge', tag_type:'building',
        action:'Place Materials Forge. 0.5/s base output (slower — materials take time). 2 molecule input slots. Craft times: 300–500s per alloy.',
        cost:{t:'cost',v:'50,000e'}, reward:{t:'reward',v:'Steel 1.5M/unit · Carbon Fiber 6M/unit'},
        time:'~3 min', advance:'materials_science unlocked' },
      { id:'Unlock Advanced Materials', tag_type:'gate',
        action:'Buy Advanced Materials (800K). Requires Materials Science + Precious Metals. Unlocks Semiconductor Wafer, Aerogel, Superconductor, Metamaterial.',
        cost:{t:'cost',v:'−800,000e',bold:true}, reward:{t:'reward',v:'Si Wafer 100M · Aerogel 400M · Superconductor 1.6B · Metamaterial 6.4B'},
        time:'~4–5 prestige runs', advance:'advanced_materials unlocked' },
      { id:'Produce advanced materials',
        action:'Chain Molecular Synthesizer → Materials Forge. Metamaterial requires Superconductor + Semiconductor Wafer — multiple forge setups needed for throughput.',
        cost:{t:'neutral',v:'Molecules + power'}, reward:{t:'reward',v:'Metamaterial 6.4B/unit — prestige wall trivially crossed'},
        time:'Ongoing', advance:'automated' },
    ]
  },
  {
    phase: 14, label: '⑭ Component Engineering',
    color: '#4a3a1c', bg: '#231a0a',
    meta: 'Unlock Tier 5 megastructure components · teaches: Component Fabricator, deep input chains, endgame entropy density',
    entropy_note: { text: 'GATE: 5M (component_engineering) + Transuranic · QP = 50B · Plasma Ring = 200B · Antimatter Cell = 800B', color: '#FFEAA7' },
    col_last: 'Unlock Condition',
    steps: [
      { id:'Unlock Transuranic Synthesis', tag_type:'gate',
        action:'Buy Transuranic Synthesis (500K). Requires Heavy Elements + Exotic Metals. Unlocks Plutonium synthesis — 2,621,440e per atom.',
        cost:{t:'cost',v:'−500,000e',bold:true}, reward:{t:'reward',v:'Plutonium ×2,621,440e — highest Tier 2 sell value'},
        time:'~4–5 prestige runs', advance:'transuranic_elements unlocked' },
      { id:'Unlock Component Engineering', tag_type:'gate',
        action:'Buy Component Engineering (5M). Requires Advanced Materials + Transuranic Synthesis. Unlocks Component Fabricator and first 3 component recipes.',
        cost:{t:'cost',v:'−5,000,000e',bold:true}, reward:{t:'reward',v:'Component Fabricator · Quantum Processor · Plasma Ring · Antimatter Cell'},
        time:'~5–6 prestige runs', advance:'component_engineering unlocked' },
      { id:'Build Component Fabricator', tag_type:'building',
        action:'Place Component Fabricator. 0.25/s base output (very slow). Craft times: 1200–2000s per component. Requires enormous power — plan generator scaling.',
        cost:{t:'cost',v:'5,000,000e'}, reward:{t:'reward',v:'Quantum Processor 50B/unit · Plasma Ring 200B/unit'},
        time:'~5 min setup', advance:'component_engineering unlocked' },
      { id:'Quantum Domains — Lepton Storm', tag_type:'milestone',
        action:'A third build site — Lepton Storm (electron/lepton-dense, no quark fields) — can be anchored for 10M entropy, sitting just past the Component Engineering gate. Like all domains, the unlock survives prestige.',
        cost:{t:'cost',v:'−10,000,000e'}, reward:{t:'reward',v:'3rd build site · electron-rich field distribution'},
        time:'—', advance:'10M entropy held' },
    ]
  },
  {
    phase: 15, label: '⑮ Megastructure Theory — Dyson Sphere',
    color: '#5c4a1c', bg: '#2b1f0a',
    meta: 'The current endgame — assemble Dyson Nodes, Orbital Frames, Graviton Lenses',
    entropy_note: { text: 'GATE: 50M (megastructure_theory) · Dyson Node = 5T · Orbital Frame = 20T · Graviton Lens = 100T', color: '#f0883e' },
    col_last: 'Unlock Condition',
    steps: [
      { id:'Unlock Megastructure Theory', tag_type:'gate',
        action:'Buy Megastructure Theory (50M). Unlocks the three Dyson sphere component recipes in the Component Fabricator.',
        cost:{t:'cost',v:'−50,000,000e',bold:true}, reward:{t:'reward',v:'Dyson Node 5T · Orbital Frame 20T · Graviton Lens 100T'},
        time:'~6–8 prestige runs', advance:'megastructure_theory unlocked' },
      { id:'Produce Dyson components',
        action:'Dyson Node requires 2× Quantum Processor + 2× Plasma Containment Ring (craft time 3000s). Orbital Frame chains from Dyson Node. Graviton Lens = final combination.',
        cost:{t:'neutral',v:'Materials + 50M+ eV power'}, reward:{t:'reward',v:'Graviton Lens 100T/unit — current endgame item'},
        time:'Ongoing', advance:'automated' },
    ]
  },
],

// ─────────────────────────────────────── DIALOGUES ──
dialogues: [
  {
    phaseLabel: '① Learning the Loop — Steps 0–4',
    entries: [
      { step_ref:'Step 0', dlg_id:'intro_architect', step_label:'intro_dialogue · on_enter · 4 lines',
        lines:[
          { text:'Welcome. I am The Architect — your guide through the Quantum Foundry. This facility builds matter from scratch, starting with the smallest particles in existence.', flags:['pause_game'] },
          { text:'Everything solid in the universe — planets, stars, you — began as fundamental particles. Your job is to harvest them, combine them, and build upward.', flags:['pause_game'] },
          { text:'See that glowing blue field? That is a Lepton Field — saturated with free electrons. Every atom you will ever build needs them.', flags:['pause_game','highlight: electron_field'] },
          { text:'Tap it. Pull electrons into your inventory.', flags:['game live','highlight: electron_field','action: pulse_field'] },
        ]
      },
      { step_ref:'Step 1', dlg_id:'first_electron_intro', step_label:'collect_first_electron · on_enter · 1 line',
        lines:[
          { text:'The field glows where particles accumulate. Tap it to pull electrons into your inventory.', flags:['game live','highlight: electron_field'] },
        ]
      },
      { step_ref:'Step 2', dlg_id:'demon_intro', step_label:'direct_to_maxwells_demon · on_enter · 1 line',
        lines:[
          { text:"Maxwell's Demon extracts entropy from the energy difference between sorted particles. Deposit what you have collected.", flags:['game live','highlight: maxwells_demon'] },
        ]
      },
      { step_ref:'Step 3', dlg_id:'sell_electrons_intro', step_label:'sell_electrons_in_demon · on_enter · 1 line',
        lines:[
          { text:'Drag the electrons to the deposit side. The Demon converts particle energy into entropy — the base currency of this facility.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 4', dlg_id:'close_demon_first', step_label:'close_demon_panel · on_enter · 1 line',
        lines:[
          { text:'Good. Those electrons built your first entropy. It is a small number for now.', flags:['game live'] },
        ]
      },
    ]
  },
  {
    phaseLabel: '② Quarks — Steps 5–9',
    entries: [
      { step_ref:'Step 5', dlg_id:'quark_field_explanation', step_label:'explain_quark_field · on_enter · 2 lines',
        lines:[
          { text:'Good work. Those electrons gave you your first entropy — the currency that powers this facility. But entropy alone cannot build matter.', flags:['pause_game'] },
          { text:'That orange field is a Quark Field. It generates Up Quarks and Down Quarks in equal measure every time you mine it — the raw ingredients for every proton and neutron you will ever craft.', flags:['pause_game','highlight: quark_field'] },
        ]
      },
      { step_ref:'Step 7', dlg_id:'demon_quarks_intro', step_label:'direct_to_maxwells_demon_quarks · on_enter · 1 line',
        lines:[
          { text:'Quarks carry more energy potential than leptons. The Demon will extract more entropy from them. Bring them in.', flags:['game live','highlight: maxwells_demon'] },
        ]
      },
      { step_ref:'Step 8', dlg_id:'sell_quarks_intro', step_label:'sell_quarks_in_demon · on_enter · 1 line',
        lines:[
          { text:'Deposit all of them. Notice how much more entropy quarks generate compared to electrons. Organized matter will generate more still.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 9', dlg_id:'close_demon_quarks', step_label:'close_demon_panel_quarks · on_enter · 1 line',
        lines:[
          { text:'You have enough to begin. Open the menu on the left — the Research panel is there.', flags:['game live'] },
        ]
      },
    ]
  },
  {
    phaseLabel: '③ Recombination I — Steps 10–12',
    entries: [
      { step_ref:'Step 10', dlg_id:'research_panel_intro', step_label:'open_research_drawer · on_enter · 1 line',
        lines:[
          { text:'The research tree is your progression map. Each node unlocks new processes, new materials, new machinery. Nothing in this facility advances without it.', flags:['pause_game'] },
        ]
      },
      { step_ref:'Step 11', dlg_id:'recombination_i_context', step_label:'buy_recombination_i · on_enter · 1 line',
        lines:[
          { text:'Recombination I. This unlocks the strong nuclear force — the force that binds quarks into nucleons. Purchase it.', flags:['pause_game'] },
        ]
      },
      { step_ref:'Step 12', dlg_id:'recombination_unlocked_dialogue', step_label:'recombination_unlocked · on_enter · 2 lines',
        lines:[
          { text:'Recombination I is active. The strong nuclear force is now at your disposal — the most powerful fundamental force in nature, and the one that makes atomic structure possible.', flags:['pause_game'] },
          { text:'A proton is two Up Quarks and one Down Quark — net charge +1, the core of every hydrogen atom. A neutron is one Up Quark and two Down Quarks — net charge zero, the stabilizing mass inside every nucleus beyond hydrogen.', flags:['pause_game'] },
        ]
      },
    ]
  },
  {
    phaseLabel: '④ Nucleons — Steps 13–19',
    entries: [
      { step_ref:'Step 14', dlg_id:'open_recipe_panel_context', step_label:'open_recipe_panel_for_nucleons · on_enter · 1 line',
        lines:[
          { text:'The recipe panel holds every synthesis process you have unlocked. Open it and find the proton recipe.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 15', dlg_id:'craft_protons_context', step_label:'craft_two_protons · on_enter · 1 line',
        lines:[
          { text:'Two Up Quarks, one Down Quark, bound by the strong force. The result: a proton. Charge +1. The nucleus of the simplest atom in existence. Craft two.', flags:['pause_game'] },
        ]
      },
      { step_ref:'Step 16', dlg_id:'craft_neutrons_context', step_label:'craft_two_neutrons · on_enter · 1 line',
        lines:[
          { text:'Now the neutron — one Up Quark, two Down Quarks. Net charge zero, but nearly identical mass to the proton. Inside a nucleus, neutrons neutralize the repulsion between protons. Craft two.', flags:['pause_game'] },
        ]
      },
      { step_ref:'Step 17', dlg_id:'nucleons_to_demon_context', step_label:'direct_to_demon_for_nucleons · on_enter · 1 line',
        lines:[
          { text:'Protons and neutrons carry significantly more energy than raw quarks. The Demon values organized matter — each layer of complexity compounds your entropy yield. Bring them in.', flags:['pause_game','highlight: maxwells_demon'] },
        ]
      },
      { step_ref:'Step 18', dlg_id:'sell_nucleons_context', step_label:'sell_protons_and_neutrons · on_enter · 1 line',
        lines:[
          { text:'Deposit both the protons and the neutrons. You have built the first organized matter this facility has produced.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 19', dlg_id:'close_demon_nucleons_context', step_label:'close_demon_after_nucleons · on_enter · 1 line',
        lines:[
          { text:'Complexity yields more entropy. That principle will drive every decision as this facility scales.', flags:['game live'] },
        ]
      },
    ]
  },
  {
    phaseLabel: '⑤ Hydrogen Synthesis — Steps 20–27',
    entries: [
      { step_ref:'Step 20', dlg_id:'unlock_hydrogen_synthesis', step_label:'buy_hydrogen_synthesis · on_enter · 1 line',
        lines:[
          { text:"Excellent work. You've synthesized protons and neutrons and converted them to entropy — the fundamental energy of this facility.", flags:['pause_game'] },
        ]
      },
      { step_ref:'Step 21', dlg_id:'gather_hydrogen_context', step_label:'gather_for_hydrogen · on_enter · 2 lines',
        lines:[
          { text:'Hydrogen synthesis is unlocked. To build a hydrogen atom you need a proton — which you now know how to build — and one electron from the Lepton Field.', flags:['pause_game'] },
          { text:'Gather your materials: 4 Up Quarks and 2 Down Quarks to build two protons, plus 2 electrons to complete the atoms.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 22', dlg_id:'craft_protons_for_h_context', step_label:'craft_protons_for_hydrogen · on_enter · 1 line',
        lines:[
          { text:'Build the protons first. Two Up Quarks, one Down Quark each. Two protons means two hydrogen atoms.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 23', dlg_id:'craft_hydrogen_context', step_label:'craft_hydrogen · on_enter · 1 line',
        lines:[
          { text:'The final step: combine a proton and an electron. The electromagnetic attraction between the positive nucleus and the negative lepton creates a stable atomic orbital. Craft your hydrogen.', flags:['pause_game'] },
        ]
      },
      { step_ref:'Step 24', dlg_id:'hydrogen_crafted_dialogue', step_label:'hydrogen_crafted · on_enter · 1 line · 🎉 milestone',
        lines:[
          { text:'Hydrogen. Atomic number 1. The most abundant element in the observable universe — and you built two of them from quarks and energy alone.', flags:['pause_game'] },
        ]
      },
      { step_ref:'Step 25', dlg_id:'hydrogen_to_demon', step_label:'direct_to_demon_for_hydrogen · on_enter · 1 line',
        lines:[
          { text:'Hydrogen atoms carry more bound energy than raw particles. Organized matter always yields more entropy — the Demon knows the difference. Bring it in.', flags:['game live','highlight: maxwells_demon'] },
        ]
      },
    ]
  },
  {
    phaseLabel: '⑥ Automation — Steps 28–32',
    entries: [
      { step_ref:'Step 28', dlg_id:'intro_automation_i', step_label:'intro_automation_i · on_enter · 3 lines',
        lines:[
          { text:'You have enough entropy to unlock Automation I — your first engineering breakthrough. It enables the Harvester: a field collector that gathers particles on a continuous cycle, no tapping required.', flags:['pause_game'] },
          { text:'This is the transition point. Manual harvesting was a lesson. The Harvester is how you scale.', flags:['pause_game'] },
          { text:'Open the Research panel and purchase Automation I.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 29', dlg_id:'buy_automation_i_context', step_label:'buy_automation_i · on_enter · 1 line',
        lines:[
          { text:'Automation I. Purchase it — then you can place your first Harvester.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 30', dlg_id:'place_building_context', step_label:'place_first_building · on_enter · 1 line',
        lines:[
          { text:'Open the build menu and place a building on an open grid tile. It will begin production immediately.', flags:['pause_game'] },
        ]
      },
      { step_ref:'Step 31', dlg_id:'place_conveyor_context', step_label:'place_conveyor · on_enter · 1 line',
        lines:[
          { text:"The Harvester collects into its output buffer. A conveyor carries those particles to Maxwell's Demon automatically. Connect the two — drag a conveyor from the Harvester's output port to the Demon's input.", flags:['game live'] },
        ]
      },
      { step_ref:'Step 32', dlg_id:'automation_context', step_label:'automation_started · on_enter · 2 lines',
        lines:[
          { text:'Your factory is running. Buildings produce at their rated output continuously — no manual input required.', flags:['pause_game'] },
          { text:'Electrons are selling. But your quark fields are wasting potential — every raw quark that reaches the Demon is worth 1 entropy. Combine them into protons and neutrons first. They sell for 3.', flags:['game live'] },
        ]
      },
    ]
  },
  {
    phaseLabel: 'Phase ⑥ cont. — Nucleon Pipeline (Steps 33–41)',
    entries: [
      { step_ref:'Step 33', dlg_id:'intro_sfc_context', step_label:'intro_sfc · on_enter · 1 line',
        lines:[
          { text:'Your electron pipeline is running. You already have Recombination I — the Strong Force Combiner is available to place. Keep accumulating entropy and we will put those quarks to work.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 34', dlg_id:'place_sfc_context', step_label:'place_sfc · on_enter · 1 line',
        lines:[
          { text:'Place the Strong Force Combiner anywhere on the grid. It will show as unpowered until you connect a generator — we will handle that next.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 35', dlg_id:'place_generator_context', step_label:'place_generator · on_enter · 1 line',
        lines:[
          { text:"The Generator produces electron volts — a local power field. Any powered building within its radius draws from it automatically. Place one near the Combiner.", flags:['game live'] },
        ]
      },
      { step_ref:'Step 36', dlg_id:'link_generator_context', step_label:'link_generator · on_enter · 1 line',
        lines:[
          { text:"Select the Combiner, then drag a power link to the Generator. The Combiner draws 10 eV — well within the Generator's 50 eV output. It will come online immediately.", flags:['game live'] },
        ]
      },
      { step_ref:'Step 37', dlg_id:'place_quark_harvester_context', step_label:'place_quark_harvester · on_enter · 1 line',
        lines:[
          { text:'The Combiner is powered but has nothing to process. Place a second Harvester on the quark field — it produces Up Quarks and Down Quarks in equal measure.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 38', dlg_id:'route_quarks_context', step_label:'route_quarks_to_sfc · on_enter · 1 line',
        lines:[
          { text:"Connect the quark Harvester's output port to the Combiner's input port with a conveyor. Quarks will flow in continuously.", flags:['game live'] },
        ]
      },
      { step_ref:'Step 39', dlg_id:'configure_sfc_context', step_label:'configure_sfc_recipes · on_enter · 1 line',
        lines:[
          { text:'The Combiner can run both recipes at once. Enable Proton — 2 Up, 1 Down — and Neutron — 1 Up, 2 Down — simultaneously. With equal quark input, they balance each other naturally. No surplus accumulates.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 40', dlg_id:'route_nucleons_context', step_label:'route_nucleons_to_demon · on_enter · 1 line',
        lines:[
          { text:"Two output ports — one for protons, one for neutrons. Connect both to Maxwell's Demon. They sell for 3 entropy each.", flags:['game live'] },
        ]
      },
      { step_ref:'Step 41', dlg_id:'nucleon_loop_context', step_label:'nucleon_loop_complete · on_enter · 2 lines',
        lines:[
          { text:'Protons and neutrons selling automatically alongside electrons. Your factory has two income streams now. Every quark from every field finds a buyer.', flags:['pause_game'] },
          { text:'The Atom Generator is available — you researched it when you unlocked Hydrogen Synthesis. Save up 350 entropy and place it.', flags:['game live'] },
        ]
      },
    ]
  },
  {
    phaseLabel: 'Phase ⑥ cont. — Hydrogen Automation (Steps 42–47)',
    entries: [
      { step_ref:'Step 42', dlg_id:'intro_atom_gen_context', step_label:'intro_atom_gen · on_enter · 1 line',
        lines:[
          { text:'Nucleons are selling. But a proton and an electron together can become something more. The Atom Generator takes one of each and produces hydrogen — your first complete atom. It is ready to place.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 43', dlg_id:'place_atom_gen_context', step_label:'place_atom_generator · on_enter · 1 line',
        lines:[
          { text:"Place the Atom Generator on the grid. It draws 5 eV of power — well within your generator's current output. No new infrastructure needed.", flags:['game live'] },
        ]
      },
      { step_ref:'Step 44', dlg_id:'delete_conveyor_context', step_label:'delete_reroute_conveyors · on_enter · 2 lines',
        lines:[
          { text:'Tap any conveyor to select it — a trash icon appears on the belt. Tap the icon to remove it. This is how you redesign your pipeline.', flags:['game live'] },
          { text:'Delete the proton conveyor from the SFC to the Demon, and the electron conveyor from the Harvester to the Demon. Free those ports for the Atom Generator.', flags:['game live'] },
        ]
      },
      { step_ref:'Step 45', dlg_id:'connect_atom_gen_context', step_label:'connect_atom_gen_inputs · on_enter · 1 line',
        lines:[
          { text:"Re-route the proton output from the Demon to the Atom Generator's proton input. Do the same for the electron Harvester output. Neutrons continue to sell — they are not needed for hydrogen.", flags:['game live'] },
        ]
      },
      { step_ref:'Step 46', dlg_id:'route_hydrogen_context', step_label:'route_hydrogen_to_demon · on_enter · 1 line',
        lines:[
          { text:"Connect the Atom Generator's output to Maxwell's Demon. Hydrogen sells for 5 entropy. A raw proton and electron would have sold for 4 — assembly adds one entropy per cycle, and that margin compounds as you scale.", flags:['game live'] },
        ]
      },
      { step_ref:'Step 47', dlg_id:'hydrogen_loop_context', step_label:'hydrogen_loop_complete · on_enter · 2 lines',
        lines:[
          { text:'Hydrogen. One proton, one electron — the simplest atom in existence, and the most abundant in the universe. Your factory is making it continuously, without your input.', flags:['pause_game'] },
          { text:'Every element above hydrogen is built the same way — more protons, more neutrons, more complexity. When you have enough entropy, research Atomic Assembly. The periodic table opens from there.', flags:['game live'] },
        ]
      },
    ]
  },
  {
    phaseLabel: '⑥ cont. — Building Upgrades (Steps 48–50)',
    entries: [
      { step_ref:'Step 48', dlg_id:'intro_upgrades_context', step_label:'intro_building_upgrades · on_enter · 4 lines · highlight: atomic_assembler',
        lines:[
          { text:'Before you start saving — your factory is running at base efficiency. Every building here can be upgraded on two separate tracks: Speed and Storage.', flags:['pause_game'] },
          { text:'Speed upgrades multiply throughput. Level 2 doubles your production rate. Level 4 brings you to eight times the base. Same inputs, same power draw — delivered faster.', flags:['game live'] },
          { text:'Storage upgrades expand the output buffer — how many items a building holds before it stalls. A larger buffer means the factory keeps running longer while you are away.', flags:['game live'] },
          { text:'Tap the Atom Generator. We are upgrading its speed first — it is the bottleneck of your hydrogen line.', flags:['game live','highlight: atomic_assembler'] },
        ]
      },
      { step_ref:'Step 50', dlg_id:'upgrade_complete_context', step_label:'upgrade_context · on_enter · 3 lines · 🎉 tutorial ends',
        lines:[
          { text:'Two times throughput. The Atom Generator now produces hydrogen at twice the rate for the same input cost. Your entropy income just doubled.', flags:['pause_game'] },
          { text:'The Storage track works the same way — tap the building again and you will see it below the Speed upgrade. A larger output buffer means the line keeps running while you are offline.', flags:['game live'] },
          { text:'Both tracks reset on prestige. The entropy you invest here comes back as a discount on your next run — every upgrade makes the next cycle faster.', flags:['game live'] },
        ]
      },
    ]
  },
  {
    phaseLabel: '⑧ Atomic Assembly — Post-Tutorial',
    entries: [
      { step_ref:'post_tutorial_intro', dlg_id:'post_tutorial_intro', step_label:'on_enter · 2 lines',
        lines:[
          { text:'Your factory runs on its own. Hydrogen flows from quark to atom to entropy — that is the foundation. Everything that follows builds on this loop.', flags:['pause_game'] },
          { text:'The Research panel shows your next frontier. Atomic Assembly opens the rest of the periodic table — but it costs five hundred entropy. Let the factory accumulate it.', flags:['game live'] },
        ]
      },
      { step_ref:'buy_atomic_assembly', dlg_id:'intro_atomic_assembly_context', step_label:'on_enter · 1 line',
        lines:[
          { text:'Atomic Assembly. Every element above hydrogen follows the same principle: proton count defines the element, neutron count defines its isotope. The Atom Generator supports all of them once this is purchased.', flags:['pause_game'] },
        ]
      },
      { step_ref:'atomic_assembly_unlocked', dlg_id:'atomic_assembly_unlocked_context', step_label:'on_enter · 2 lines',
        lines:[
          { text:'Atomic Assembly is active. Helium-4 is now craftable — two protons, two neutrons, two electrons. It sells for twenty entropy: four times hydrogen, for roughly four times the inputs.', flags:['pause_game'] },
          { text:'Lithium, Carbon, Oxygen, Silicon, and Iron are also available in the recipe panel. Each one is more complex and more valuable. Start with Helium-4 and scale up from there.', flags:['game live'] },
        ]
      },
      { step_ref:'first_element_context', dlg_id:'first_element_crafted_context', step_label:'on_enter · 2 lines · 🎉 milestone',
        lines:[
          { text:'Helium-4. The second element. Formed in the first three minutes after the Big Bang through primordial nucleosynthesis — you have just replicated one of the universe\'s oldest reactions.', flags:['pause_game'] },
          { text:'Notice the sell value: twenty entropy per atom. The complexity premium scales with atomic mass. Heavier elements cost more to build, but they are proportionally more valuable to sell.', flags:['game live'] },
        ]
      },
    ]
  },
  {
    phaseLabel: '⑨ Heavy Elements & Uranium',
    entries: [
      { step_ref:'buy_heavy_elements', dlg_id:'intro_heavy_elements_context', step_label:'on_enter · 2 lines',
        lines:[
          { text:'Heavy Elements research unlocks Uranium — 92 protons, 146 neutrons, 92 electrons. Atomic mass 238. Craft time: nearly four minutes. Power cost: 1,190 electron volts. Sell value: 1,000 entropy.', flags:['pause_game'] },
          { text:'Your current generators likely cannot sustain 1,190 eV. A fully upgraded Basic Generator outputs 400 eV. You will need multiple before attempting Uranium synthesis.', flags:['game live'] },
        ]
      },
      { step_ref:'heavy_elements_unlocked', dlg_id:'heavy_elements_unlocked_context', step_label:'on_enter · 1 line',
        lines:[
          { text:'Heavy Elements research acquired. Uranium synthesis is now available in the Atom Generator. Check your power grid before you begin — the draw is 1,190 eV per cycle.', flags:['pause_game'] },
        ]
      },
      { step_ref:'uranium_crafted', dlg_id:'uranium_crafted_context', step_label:'on_enter · 2 lines · 🎉 milestone',
        lines:[
          { text:'Uranium. 1,000 entropy for a single atom. This is the endpoint of stellar fusion — stars cannot forge anything heavier than iron without consuming more energy than they produce. What you just built required conditions found only in supernovae.', flags:['pause_game'] },
          { text:'It is also radioactive. Its isotopes — particularly Uranium-235 — are where things become truly interesting. That requires Isotope Engineering.', flags:['game live'] },
        ]
      },
    ]
  },
  {
    phaseLabel: '⑩ Isotope Engineering',
    entries: [
      { step_ref:'buy_isotope_engineering', dlg_id:'intro_isotopes_context', step_label:'on_enter · 2 lines',
        lines:[
          { text:"An element's identity is its proton count. Its isotope is its neutron count. Change the neutrons and you change the nuclear behavior — mass, stability, decay mode — without changing the element itself.", flags:['pause_game'] },
          { text:'The Isotopic Manipulator adds or removes neutrons from a base element. It runs at half the speed of the Atom Generator — neutron adjustment is precise work. Deuterium, Tritium, and Carbon-14 all unlock here.', flags:['game live'] },
        ]
      },
      { step_ref:'isotopes_unlocked', dlg_id:'isotopes_unlocked_context', step_label:'on_enter · 1 line',
        lines:[
          { text:'Isotope Engineering is active. The Isotopic Manipulator is now available to place. It needs a power connection and inputs from your existing nucleon and element pipelines.', flags:['pause_game'] },
        ]
      },
      { step_ref:'place_isotopic_manipulator', dlg_id:'place_manipulator_context', step_label:'on_enter · 1 line',
        lines:[
          { text:'Route neutrons from your Strong Force Combiner to the Manipulator\'s neutron input. For Deuterium and Tritium, route Hydrogen to the element input. For Carbon-14, route Carbon. Each isotope uses a different base element.', flags:['game live'] },
        ]
      },
      { step_ref:'isotope_crafted', dlg_id:'isotope_crafted_context', step_label:'on_enter · 2 lines · 🎉 milestone',
        lines:[
          { text:'Deuterium. Chemically identical to Hydrogen — but with an extra neutron in the nucleus. Stable isotopes like Deuterium can be stored indefinitely. Radioactive ones will not hold still.', flags:['pause_game'] },
          { text:'Radioactive isotopes emit particles as they decay — Alpha or Beta depending on the isotope. With the right containment, those particles feed back into your power grid. The next research branch handles this.', flags:['game live'] },
        ]
      },
    ]
  },
  {
    phaseLabel: '⑪ Radioactive Decay',
    entries: [
      { step_ref:'buy_radioactive_isotopes', dlg_id:'intro_radioactive_context', step_label:'on_enter · 2 lines',
        lines:[
          { text:'Uranium-235 is the fissile isotope. Enrichment removes three neutrons from Uranium-238, shifting it from nearly stable to critically unstable. The process releases an alpha particle — a helium-4 nucleus ejected from the atom.', flags:['pause_game'] },
          { text:'Before producing radioactive isotopes, you need a Radioactive Containment structure. It houses the Manipulator, absorbs decay events, and converts emitted particles into usable energy. Place it first.', flags:['game live'] },
        ]
      },
      { step_ref:'radioactive_isotopes_unlocked', dlg_id:'radioactive_unlocked_context', step_label:'on_enter · 1 line',
        lines:[
          { text:'Radioactive Isotopes research complete. The Radioactive Containment building is now available. Place it before operating the Manipulator for fissile materials — without it, decay products are uncontrolled.', flags:['pause_game'] },
        ]
      },
      { step_ref:'place_radioactive_containment', dlg_id:'place_containment_context', step_label:'on_enter · 1 line',
        lines:[
          { text:'The Containment structure occupies a 2×2 footprint and draws 30 eV. Place an Isotopic Manipulator inside it to handle radioactive synthesis. Alpha and Beta particles from decay are collected automatically and returned to your power grid.', flags:['game live'] },
        ]
      },
      { step_ref:'uranium_235_crafted', dlg_id:'uranium_235_crafted_context', step_label:'on_enter · 2 lines · 🎉 milestone',
        lines:[
          { text:'Uranium-235. Enriched nuclear fuel — 3,000 entropy per atom. The alpha particle released during enrichment is recovered by your Containment building automatically: 20 eV returned to the grid per particle.', flags:['pause_game'] },
          { text:'At scale, the decay particles emitted by your radioactive pipeline contribute meaningfully to your power budget. Alpha carries 20 eV. Beta carries 10. Let the decay work for you.', flags:['game live'] },
        ]
      },
      { step_ref:'particles_loop_complete', dlg_id:'particles_loop_context', step_label:'on_enter · 2 lines · 🎉 end of current content',
        lines:[
          { text:'Alpha and Beta particles — secondary energy currency, harvested from nuclear instability. What is a hazard in any other context is a resource here. Your Containment building converts decay into power.', flags:['pause_game'] },
          { text:'You have built matter from nothing — quarks to nucleons, nucleons to atoms, atoms to isotopes, isotopes to fissile fuel. This is the current frontier of the Quantum Foundry. The next tier of synthesis is ahead. Keep building.', flags:['game live'] },
        ]
      },
    ]
  },
],

// ─────────────────────────────────────── DESIGN GAPS ──
design_gaps: [
  { id:'gap-1', cat:'Design',
    issue:'Recombination I entropy gap',
    detail:'Guided steps earn ~15 entropy; gate costs 100. Steps 10–11 block all collection. Player is stuck.',
    default_sol:'Grant 250 starting entropy via game_config.starting_entropy → GameConfigSO.startingEntropy → PlayerInventoryAuthoring. Tutorial earns +37 more (287 total). After Recombination I (−100) + Phase 4 gains (+12) + Hydrogen Synthesis (−50) + H-sell (+10), 137 entropy remains at Phase ⑥ — enough for the Harvester (−100).',
    default_date:'2026-05-28' },
  { id:'gap-2', cat:'Design', status:'resolved',
    issue:'Prestige threshold calibrated and wall detection implemented',
    detail:'prestige_base_value raised from 500 → 5000. Wall = 5000 × 10 = 50,000e net worth. Requires reaching Copper/Nickel element range (~10,240–20,480e per item), targeting ~60–90 min first run without multipliers. PrestigeAvailable detection added to PrestigeSystem.OnUpdate — fires when NetWorth ≥ PrestigeWallValue. Mirrors ISM first-wall placement: just past first automation loop, well before T3 content.',
    default_sol:'prestige_base_value = 5000 in game_config (game_data.json + GameConfigSO). PrestigeWallValue field added to PlayerProgressData and baked from GameConfigSO in PlayerInventoryAuthoring. PrestigeSystem detects wall crossing and sets PrestigeAvailable = true.',
    default_date:'2026-06-02' },
  { id:'gap-3', cat:'Design',
    issue:'"Automation I" has no matching research node',
    detail:'intro_automation_i dialogue calls it "Automation I" as if it\'s a research unlock, but the Harvester is available from start with no research required.',
    default_sol:'Added automation_i research node (cost: 25e, branch: Engineering, prerequisites: hydrogen_synthesis, 50% prestige discount). Harvester now requires automation_i — available_from_start set to false. Dialogue updated to match. New tutorial step buy_automation_i added as step 29.',
    default_date:'2026-05-28' },
  { id:'gap-4', cat:'Design',
    issue:'Harvester placement cost undefined',
    detail:'Step 29 lists building cost as TBD. Does placing a Harvester cost entropy? Buildings have upgrade costs in game_data.json but no explicit placement cost field.',
    default_sol:'Added entropy_cost to all buildings in game_data.json. Harvester: 100, Generator: 150, Combiner: 200, Assembler: 800, Manipulator: 1500, Containment: 2500. Maxwell\'s Demon stays 0 (pre-placed). Cost deduction wired in HUDController.OnBuildingPlaced().',
    default_date:'2026-05-28' },
  { id:'gap-5', cat:'Dialogue',
    issue:'14 authored dialogues unassigned to tutorial steps',
    detail:'Includes: sell_electrons_intro, close_demon_first, research_panel_intro, recombination_i_context, nucleons_to_demon_context, gather_hydrogen_context, and 8 others. All written; none wired.',
    default_sol:'Wired 13 dialogues to their matching tutorial step on_enter.dialogue_id in game_data.json. spend_prestige_context cut (prestige is post-tutorial). See Dialogue tab "Wired" section for full mapping.',
    default_date:'2026-05-28' },
  { id:'gap-6', cat:'Visualization',
    issue:'No running entropy total in Overview',
    detail:'Each phase shows earn/spend but no cumulative column. A running total would make balance problems immediately visible across phases.',
    default_sol:'Added running-total chip row to the Entropy Economy callout showing each gate and the running balance: Start 250 → +15 → −100 → +12 → −50 → +10 → −25 → −100 → End 12e.',
    default_date:'2026-05-28' },
  { id:'gap-7', cat:'Visualization',
    issue:'Post-tutorial loop not mapped',
    detail:'Doc ends at prestige (step 33). Run 2 onward — Atomic Assembly, Isotopes, research memory discounts, prestige upgrades — has no visualization.',
    default_sol:'Added full phase mapping for Phases ⑦–⑪ in the Overview tab: ⑦ Run 2 Ramp-Up, ⑧ Atomic Assembly, ⑨ Heavy Elements (Uranium), ⑩ Isotopes, ⑪ Radioactive Decay. Each phase shows buildings, research gates, and entropy values.',
    default_date:'2026-05-28' },
  { id:'gap-8', cat:'Visualization',
    issue:'Skip-condition path not visualized',
    detail:'intro_dialogue has a skip: if recombination_i is already unlocked, jump to collect_quarks_for_nucleons. Second-run experience is different but not documented.',
    default_sol:'Tutorial ends at step 32 (automation_started) — after first prestige reset all research resets. On Run 2, intro_dialogue skip-condition fires (recombination_i already known from prestige memory) and jumps straight to collect_quarks_for_nucleons, bypassing phases 1–3. This is the intended fast re-run path.',
    default_date:'2026-05-28' },
  { id:'gap-9', cat:'Engineering',
    issue:'GridOccupancy has no test coverage',
    detail:'Spatial gatekeeper for all building and conveyor placement; used by BuildingPlacer, ConveyorPlacer, and DeconstructController. Errors here are silent — a tile can appear free while already occupied.',
    default_sol:'Add Edit Mode tests in Assets/Scripts/Tests/GridOccupancyTests.cs covering: MarkOccupied/IsOccupied round-trip, multi-cell footprint reservation, overlapping placement rejection, and release/clear.',
    default_date:'2026-05-29' },
  { id:'gap-10', cat:'Engineering',
    issue:'PortUtils has no test coverage',
    detail:'Pure static utility used by ConveyorSystem and placement controllers for port direction math and offset calculations. Port direction errors are silent — conveyors connect to the wrong face without any runtime warning.',
    default_sol:'Add Edit Mode tests in Assets/Scripts/Tests/PortUtilsTests.cs covering all static methods: direction lookups, port offset calculations, and boundary/edge-tile cases.',
    default_date:'2026-05-29' },
  { id:'gap-11', cat:'Engineering',
    issue:'PowerGridSystem has no test coverage',
    detail:'The only ECS system without tests — CollectorSystem, EntropySinkSystem, and ProductionSystem are all covered. Power-state errors (building incorrectly marked unpowered) are among the hardest to reproduce in play.',
    default_sol:'Add Edit Mode tests in Assets/Scripts/Tests/PowerGridSystemTests.cs following EntropySinkSystemTests pattern — spawn entities with PowerNodeData, tick the system, assert correct powered/unpowered state transitions.',
    default_date:'2026-05-29' },
  { id:'gap-12', cat:'Engineering',
    issue:'RecipeKnowledgeService has no interface — tight singleton coupling',
    detail:'HUDController and ResearchService call the singleton directly. Neither can be unit-tested with a mock knowledge store. Extracting an interface is a prerequisite for isolated testing of both consumers.',
    default_sol:'Extract IRecipeKnowledgeService with IsKnown(string) and MarkKnown(string). Update HUDController and ResearchService to depend on the interface. RecipeKnowledgeService remains the production singleton; tests inject a stub.',
    default_date:'2026-05-29' },
  { id:'gap-13', cat:'Engineering',
    issue:'RecipeDatabase has no execution-order guarantee relative to RecipeKnowledgeService',
    detail:'RecipeKnowledgeService.Start() calls RecipeDatabase.Instance with no DefaultExecutionOrder set on RecipeDatabase. If ordering ever shifts, SyncWithRecipeDatabase silently skips and new recipes are never added to the cross-prestige save file.',
    default_sol:'Add [DefaultExecutionOrder(-80)] to RecipeDatabase so it always initializes before RecipeKnowledgeService (-70). Makes the dependency explicit in code rather than relying on scene insertion order.',
    default_date:'2026-05-29' },
  { id:'gap-14', cat:'Design', status:'resolved',
    issue:'No in-context rotate during tap-to-place confirm flow (mobile)',
    detail:'Placement now uses tap-to-position then a world-anchored ✓/✕ confirm popup above the candidate cell. Rotate (and Flip) lived only on the bottom placement bar, so a mobile player who tapped a cell had to look away from the confirm popup to re-orient a building before accepting.',
    default_sol:'Added a ↻ Rotate button (btn-rotate-candidate) to the confirm popup between ✕ and ✓, calling the same BuildingPlacementController.Rotate() as the bottom-bar button and the R key. HUDController shows it only when CanRotate is true; the per-frame popup re-anchor + ✓-validity refresh pick up the new footprint after each rotate. Flip stays on the bottom bar. Covered by Assets/Scripts/Tests/BuildingPlacementControllerTests.cs.',
    default_date:'2026-06-15' },
],

// ─────────────────────────────────────── SIMPLE OVERVIEW ──
simple_overview: [
  { phase:1,    phaseName:'Learning the Loop',    phaseColor:'#1a5c8a', isTutorial:true,  steps:[
    { name:'Introduction',          type:'dialogue', dur:60  },
    { name:'First Electrons',       type:'action',   dur:120 },
    { name:"Meet Maxwell's Demon",  type:'building', dur:30  },
    { name:'Electron Milestone',    type:'milestone',dur:30  },
  ]},
  { phase:2,    phaseName:'Quarks',               phaseColor:'#4a5568', isTutorial:true,  steps:[
    { name:'Unlock Quark Field',    type:'gate',     dur:60  },
    { name:'Gather Quarks',         type:'action',   dur:180 },
    { name:'First Sale',            type:'milestone',dur:60  },
  ]},
  { phase:3,    phaseName:'Recombination I',      phaseColor:'#8a4a00', isTutorial:true,  steps:[
    { name:'Research Gate (100e)',  type:'gate',     dur:60  },
    { name:'Unlock Nucleon Crafting',type:'building',dur:120 },
  ]},
  { phase:4,    phaseName:'Nucleons',             phaseColor:'#4a5568', isTutorial:true,  steps:[
    { name:'Manual Crafting',       type:'action',   dur:180 },
    { name:'Value Discovery',       type:'dialogue', dur:120 },
    { name:'Nucleon Milestone',     type:'milestone',dur:120 },
  ]},
  { phase:5,    phaseName:'Hydrogen Synthesis',   phaseColor:'#4a5568', isTutorial:true,  steps:[
    { name:'Research Gate (50e)',   type:'gate',     dur:60  },
    { name:'First Atom Assembly',   type:'action',   dur:120 },
    { name:'Atom Milestone',        type:'milestone',dur:120 },
  ]},
  { phase:'6A', phaseName:'Automation Setup',     phaseColor:'#1a6e40', isTutorial:true,  steps:[
    { name:'Research Gate (25e)',   type:'gate',     dur:60  },
    { name:'Place Harvester (100e)',type:'building', dur:120 },
    { name:'First Conveyor',        type:'building', dur:120 },
  ]},
  { phase:'6B', phaseName:'Nucleon Pipeline',     phaseColor:'#1a6e40', isTutorial:true,  steps:[
    { name:'SFC Building (200e)',   type:'building', dur:120 },
    { name:'Generator (150e)',      type:'building', dur:120 },
    { name:'Pipeline Running',      type:'milestone',dur:240 },
  ]},
  { phase:'6C', phaseName:'Hydrogen Automation',  phaseColor:'#1a6e40', isTutorial:true,  steps:[
    { name:'Atom Generator (350e)', type:'building', dur:180 },
    { name:'Reroute Conveyors',     type:'action',   dur:120 },
    { name:'H Loop Running',        type:'milestone',dur:120 },
  ]},
  { phase:'6D', phaseName:'Building Upgrades',    phaseColor:'#1a6e40', isTutorial:true,  steps:[
    { name:'Learn Speed + Storage Tracks', type:'dialogue', dur:90  },
    { name:'Speed Upgrade L2 (1,000e)',    type:'gate',     dur:300 },
    { name:'Tutorial Complete',            type:'milestone',dur:90  },
  ]},
  { phase:7,    phaseName:'Run 2 Ramp-Up',        phaseColor:'#6e3a8a', isTutorial:false, steps:[
    { name:'Prestige Reset',        type:'gate',     dur:300 },
    { name:'Quick Re-unlock',       type:'action',   dur:600 },
    { name:'Optimized Pipeline',    type:'milestone',dur:300 },
  ]},
  { phase:8,    phaseName:'Atomic Assembly',      phaseColor:'#6e3a8a', isTutorial:false, steps:[
    { name:'Gate: Atomic Assembly (500e)', type:'gate',   dur:120  },
    { name:'He-4 + Li + C',               type:'action', dur:900  },
    { name:'O + Si + Fe',                 type:'action', dur:1200 },
  ]},
  { phase:9,    phaseName:'Heavy Elements',       phaseColor:'#6e3a8a', isTutorial:false, steps:[
    { name:'Gate: Heavy Elements (3000e)', type:'gate',   dur:120  },
    { name:'Uranium Production',           type:'action', dur:1800 },
  ]},
  { phase:10,   phaseName:'Isotopes',             phaseColor:'#6e3a8a', isTutorial:false, steps:[
    { name:'Gate: Isotopes (1500e)', type:'gate',   dur:120  },
    { name:'Deuterium + Tritium',    type:'action', dur:1200 },
    { name:'C-14 + U-235',          type:'action', dur:1500 },
  ]},
  { phase:11,   phaseName:'Radioactive Decay',    phaseColor:'#6e3a8a', isTutorial:false, steps:[
    { name:'Gate: Radioactive Decay (5000e)', type:'gate',     dur:120  },
    { name:'Alpha/Beta Particles',            type:'action',   dur:2400 },
    { name:'Containment (2500e)',             type:'building', dur:600  },
  ]},
  { phase:12,   phaseName:'Elements + Molecules', phaseColor:'#45B7D1', isTutorial:false, steps:[
    { name:'Gate: Light/Mid Elements (3K)',   type:'gate',     dur:120  },
    { name:'Gate: Transition→Exotic (113K)', type:'gate',     dur:300  },
    { name:'Gate: Mol. Synthesis (3K)',       type:'gate',     dur:120  },
    { name:'Molecular Synthesizer (5Ke)',     type:'building', dur:300  },
    { name:'Gate: Adv. Molecules (20K)',      type:'gate',     dur:300  },
    { name:'LH₂ → H₂O → CH₄ → UF₆',        type:'action',   dur:1800 },
  ]},
  { phase:13,   phaseName:'Materials Science',    phaseColor:'#96CEB4', isTutorial:false, steps:[
    { name:'Gate: Materials Science (150K)',  type:'gate',     dur:300  },
    { name:'Materials Forge (50Ke)',          type:'building', dur:300  },
    { name:'Steel → Carbon Fiber → Ti Alloy',type:'action',   dur:2400 },
    { name:'Gate: Adv. Materials (800K)',     type:'gate',     dur:300  },
    { name:'Semiconductor → Aerogel → SC',   type:'action',   dur:3600 },
    { name:'Metamaterial',                   type:'milestone',dur:600  },
  ]},
  { phase:14,   phaseName:'Component Engineering',phaseColor:'#FFEAA7', isTutorial:false, steps:[
    { name:'Gate: Transuranic (500K)',        type:'gate',     dur:300  },
    { name:'Gate: Component Eng. (5M)',       type:'gate',     dur:300  },
    { name:'Component Fabricator (5Me)',      type:'building', dur:300  },
    { name:'Quantum Proc. + Plasma Ring',     type:'action',   dur:3600 },
    { name:'Antimatter Cell',                type:'milestone',dur:2400 },
  ]},
  { phase:15,   phaseName:'Dyson Sphere',          phaseColor:'#f0883e', isTutorial:false, steps:[
    { name:'Gate: Megastructure Theory (50M)',type:'gate',     dur:300  },
    { name:'Dyson Node',                     type:'action',   dur:3600 },
    { name:'Orbital Frame',                  type:'action',   dur:4800 },
    { name:'Graviton Lens (endgame)',         type:'milestone',dur:6000 },
  ]},
],

// ─────────────────────────────────── PRESTIGE SHOP ──
// Formula: PC = floor(max(0, log10(netWorth / formulaBase) × formulaScale))
// Sync formulaBase / formulaScale / wallMultiplier with game_data.json game_config
prestige: {
  formulaBase:      5000,
  formulaScale:     50,
  wallMultiplier:   10,

  upgrades: [
    // ── Tier 1: no prerequisites ──────────────────────────────────────────
    {
      id: 'entropy_headstart', name: 'Entropy Headstart', tier: 1,
      effectType: 'StartingEntropyBonus', effectPerLevel: 250, unit: 'e',
      maxLevel: 5, baseCost: 5, costScaling: 2.0,
      costs: [5, 10, 20, 40, 80],
      prereqs: [],
      description: 'Start each run with +250 extra entropy per level.',
    },
    {
      id: 'memory_resonance', name: 'Memory Resonance', tier: 1,
      effectType: 'GlobalResearchDiscount', effectPerLevel: 5, unit: '%',
      maxLevel: 5, baseCost: 10, costScaling: 2.0,
      costs: [10, 20, 40, 80, 160],
      prereqs: [],
      description: 'All research costs 5% less per level on every run.',
    },
    {
      id: 'assembly_line', name: 'Assembly Line', tier: 1,
      effectType: 'CraftSpeedMultiplier', effectPerLevel: 10, unit: '%',
      maxLevel: 5, baseCost: 15, costScaling: 2.0,
      costs: [15, 30, 60, 120, 240],
      prereqs: [],
      description: 'All buildings craft 10% faster per level.',
    },
    {
      id: 'expanded_vault', name: 'Expanded Vault', tier: 1,
      effectType: 'VaultCapacity', effectPerLevel: 20, unit: 'slots',
      maxLevel: 5, baseCost: 25, costScaling: 1.5,
      costs: [25, 38, 56, 84, 126],
      prereqs: [],
      description: 'Increase inventory capacity by +20 slots per level.',
    },
    {
      id: 'field_cooldown', name: 'Quick Hands', tier: 1,
      effectType: 'FieldCooldownReduction', effectPerLevel: 5, unit: '%',
      maxLevel: 10, baseCost: 20, costScaling: 1.5,
      costs: [20, 35, 55, 85, 130, 200, 305, 465, 705, 1070],
      prereqs: [],
      description: 'Field tap cooldown is 5% shorter per level (max −50%).',
    },
    // ── Tier 2: require 1 Tier-1 level ──────────────────────────────────
    {
      id: 'quantum_yield', name: 'Quantum Yield', tier: 2,
      effectType: 'OutputQuantityMultiplier', effectPerLevel: 10, unit: '%',
      maxLevel: 5, baseCost: 40, costScaling: 2.0,
      costs: [40, 80, 160, 320, 640],
      prereqs: [{ id: 'assembly_line', minLevel: 1 }],
      description: 'All recipes produce +10% more output per level.',
    },
    {
      id: 'efficient_layouts', name: 'Efficient Layouts', tier: 2,
      effectType: 'BuildingCostReduction', effectPerLevel: 5, unit: '%',
      maxLevel: 6, baseCost: 35, costScaling: 2.0,
      costs: [35, 70, 140, 280, 560, 1120],
      prereqs: [{ id: 'memory_resonance', minLevel: 1 }],
      description: 'Building placement costs 5% less per level (max −30%).',
    },
    // ── Tier 3: require deeper investment ───────────────────────────────
    {
      id: 'turnkey_builder', name: 'Turnkey Builder', tier: 3,
      effectType: 'BuildingStartPrePlaced', effectPerLevel: 1, unit: 'harvester',
      maxLevel: 3, baseCost: 150, costScaling: 5.0,
      costs: [150, 750, 3750],
      prereqs: [{ id: 'efficient_layouts', minLevel: 2 }],
      description: 'Start each run with +1 pre-placed Harvester per level.',
    },
    {
      id: 'research_overdrive', name: 'Research Overdrive', tier: 3,
      effectType: 'ResearchSpeed', effectPerLevel: 5, unit: '%',
      maxLevel: 10, baseCost: 30, costScaling: 1.65,
      costs: [30, 50, 85, 140, 230, 380, 625, 1030, 1700, 2800],
      prereqs: [{ id: 'memory_resonance', minLevel: 2 }],
      description: 'Research timers complete 5% faster per level (max -50%).',
    },
    {
      id: 'entropy_echo', name: 'Entropy Echo', tier: 3,
      effectType: 'PrestigeGainMultiplier', effectPerLevel: 5, unit: '%',
      maxLevel: 4, baseCost: 200, costScaling: 3.0,
      costs: [200, 600, 1800, 5400],
      prereqs: [{ id: 'entropy_headstart', minLevel: 3 }, { id: 'memory_resonance', minLevel: 3 }],
      description: 'Earn +5% more prestige currency per run per level.',
    },
  ],
},

}; // end LOOP_DATA
