# Keel as the interface between mods and Sea Power

- Mods call only the stable Keel API; Keel talks to the game
- Small game change: permanent adapter, stays for years
- Crazy game change: timed adapter, about 1 year, then retired (Keel warns the modder)
- Keel extras: features the game lacks, built by Keel from game parts
- `Foo` / `Bar` / `Baz` / `Convoy` are placeholders, not real game functions
- Part of Wave 3 of the [roadmap](ROADMAP.md)
- The diagrams are [Mermaid](https://mermaid.js.org) code: GitHub and Obsidian draw them; a picture for a post: `mmdc -i diagrams.md -o diagrams.svg` ([Mermaid CLI](https://github.com/mermaid-js/mermaid-cli))

## Component: where Keel sits

```mermaid
---
title: Keel as the interface between mods and Sea Power
config:
  theme: neutral
  flowchart:
    curve: linear
---
flowchart LR
    Mod["<b>Mod</b><br/>modder's code"]

    subgraph Keel["Keel «component»"]
        direction TB
        API["<b>Stable Keel API</b><br/>what mods call, never breaks"]
        subgraph Adapters["Adapter layer"]
            direction LR
            Perm["<b>Permanent adapter</b><br/>small change, no expiry"]
            Timed["<b>Timed adapter</b><br/>crazy change, 1 year expiry"]
        end
        Extras["<b>Extras module</b><br/>features the game lacks"]
        API --> Perm
        API --> Timed
        API --> Extras
    end

    Game["<b>Sea Power</b><br/>game API, per build"]

    Mod -- "uses" --> API
    API -. "unchanged: pass-through" .-> Game
    Perm -. "Foo() runs on Bar()" .-> Game
    Timed -. "Foo() runs on Baz()" .-> Game
    Extras -. "built from game parts" .-> Game

    classDef perm fill:#E1F5EE,stroke:#0F6E56,color:#085041
    classDef timed fill:#FAECE7,stroke:#993C1D,color:#712B13
    classDef extra fill:#EEEDFE,stroke:#534AB7,color:#3C3489
    class Perm perm
    class Timed timed
    class Extras extra
```

## A call through Keel, per case

```mermaid
---
title: A mod call through Keel, per case
config:
  theme: neutral
  themeVariables:
    noteBkgColor: "#ffffff"
    noteTextColor: "#222222"
    noteBorderColor: "#888888"
  sequence:
    mirrorActors: false
    actorMargin: 140
    noteMargin: 14
    wrap: false
---
sequenceDiagram
    participant M as Mod
    participant K as Keel
    participant G as Sea Power

    alt 1. unchanged
        M->>K: Foo()
        K->>G: Foo()
        G-->>K: result
        K-->>M: result
    else 2. small change: permanent adapter
        M->>K: Foo()
        rect rgb(225, 245, 238)
            Note over K: Permanent adapter<br/>Foo() runs on Bar()<br/>no expiry
        end
        K->>G: Bar()
        G-->>K: result
        K-->>M: result as Foo()
    else 3. crazy change: timed adapter
        M->>K: Foo()
        rect rgb(250, 236, 231)
            Note over K: Timed adapter<br/>Foo() runs on Baz()<br/>expires after 1 year
        end
        K-->>M: warning: Foo() retired in 247 days
        K->>G: Baz()
        G-->>K: result
        K-->>M: result as Foo()
    else 3b. after expiry
        M->>K: Foo()
        K--xM: refused: use Baz()
    else 4. Keel extra
        M->>K: Keel.Convoy()
        rect rgb(238, 237, 254)
            Note over K: Keel extra<br/>not in the game
        end
        K->>G: part A
        K->>G: part B
        G-->>K: results
        K-->>M: convoy result
    end
```

## Life of a Keel API function

```mermaid
---
title: Life of a Keel API function
config:
  theme: neutral
---
stateDiagram-v2
    direction LR

    [*] --> PassThrough: Keel offers Foo()
    PassThrough: Pass-through
    PassThrough: Foo() calls the game's Foo()

    PassThrough --> Decide: game update changes Foo()
    state Decide <<choice>>

    Decide --> Permanent: small change, cheap to keep
    Permanent: Permanent adapter
    Permanent: Foo() runs on Bar(), no expiry

    Decide --> Timed: crazy change, too costly to fake
    Timed: Timed adapter
    Timed: Foo() runs on Baz(), Keel card warns the modder

    Timed --> Retired: 1 year passed
    Retired: Retired
    Retired: Foo() refused, use Baz()
    Retired --> [*]

    Permanent --> Decide: next game update changes it again

    classDef perm fill:#E1F5EE,stroke:#0F6E56,color:#085041
    classDef timed fill:#FAECE7,stroke:#993C1D,color:#712B13
    classDef gone fill:#F1EFE8,stroke:#5F5E5A,color:#444441,stroke-dasharray:4 3
    class Permanent perm
    class Timed timed
    class Retired gone
```
