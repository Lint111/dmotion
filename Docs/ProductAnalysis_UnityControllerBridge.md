# Unity Controller Bridge - Product Market Analysis

**Document Version:** 1.0
**Date:** 2026-01-15
**Status:** Analysis Complete

---

## Executive Summary

This document analyzes the commercial viability of selling a **Unity Controller Bridge** as a closed-source Unity plugin while maintaining DMotion as an open-source animation framework. The bridge converts standard Unity AnimatorControllers to DMotion's ECS-based state machines, enabling developers to migrate existing projects to DOTS without rewriting animation logic.

**Key Finding:** The product addresses a genuine market gap with limited competition. The niche market size (~25,000-150,000 potential users) presents moderate revenue potential ($20-60K Year 1). Using a **separate works licensing model** — keeping DMotion open source while selling the Bridge as an independent proprietary extension — provides a legally sound path forward. This is standard industry practice (WordPress plugins, Unity Asset Store packages). Recommend proceeding with tiered pricing ($49-99) and brief legal consultation to confirm approach.

---

## Table of Contents

1. [Market Size Analysis](#1-market-size-analysis)
2. [Competitive Landscape](#2-competitive-landscape)
3. [Product Feature Analysis](#3-product-feature-analysis)
4. [Pricing Strategy](#4-pricing-strategy)
5. [Legal Considerations](#5-legal-considerations)
6. [Risk Assessment](#6-risk-assessment)
7. [Recommendations](#7-recommendations)
8. [Sources](#8-sources)

---

## 1. Market Size Analysis

### 1.1 Unity Developer Population

Unity dominates the game engine market with substantial developer reach:

| Metric | Value | Source |
|--------|-------|--------|
| Unity market share | 38% primary engine usage | [6sense](https://6sense.com/tech/game-development/unity-market-share) |
| Unity developers surveyed | 61% use Unity | [SlashData](https://www.slashdata.co/post/did-you-know-that-60-of-game-developers-use-game-engines) |
| Steam game share | >50% of Steam games | [VG Insights 2025 Report](https://vginsights.com/assets/reports/The_Big_Game_Engines_Report_of_2025.pdf) |
| Mobile market share | 50%+ | Unity official stats |
| AR/VR content | 60% | Unity official stats |
| Total Unity customers | ~13,874 identified companies | 6sense data |

**Estimated Total Unity Developers:** 2-3 million active developers worldwide

### 1.2 DOTS/ECS Adoption

Unity's Data-Oriented Technology Stack (DOTS) adoption remains niche but growing:

| Indicator | Estimate | Notes |
|-----------|----------|-------|
| DOTS awareness | ~30-40% of Unity developers | Based on forum engagement |
| DOTS usage (production) | ~5-10% of active developers | Estimated from community polls |
| DOTS consideration | ~15-20% evaluating | Forum activity analysis |
| Growth trajectory | Increasing post-Unity 6 | Official roadmap priority |

**Critical Pain Point:** Unity's official animation system for DOTS remains incomplete. As of Q2 2024, Unity acknowledged the animation system "has been 12 years since release" and they're evaluating major changes. Community frustration is evident:

> "What's the point of spawning 1000 game entities when you can't even animate them without 3rd party tools that are all in early or abandoned state." — [Unity Forum User](https://discussions.unity.com/t/how-can-v1-of-unity-dots-be-released-when-you-cant-have-animations/919993)

### 1.3 Total Addressable Market (TAM)

```
Total Unity Developers:           2,500,000
× DOTS/ECS Active Users:          × 7.5% (midpoint estimate)
= DOTS Developers:                187,500

× Need Animation Solution:        × 60% (character games)
= Potential Animation Users:      112,500

× Willing to Pay for Bridge:      × 30% (migration scenario)
= Addressable Market:             33,750 developers
```

**Conservative Estimate:** 25,000-50,000 potential customers
**Optimistic Estimate:** 75,000-150,000 potential customers

### 1.4 Market Trends

**Positive Indicators:**
- Unity actively pushing DOTS for performance-critical games ([Unity DOTS](https://unity.com/dots))
- Major studios using DOTS: V Rising (Stunlock), Zenith VR (Ramen VR), IXION (Kasedo)
- December 2025 ECS update shows continued Unity investment ([Unity Forums](https://discussions.unity.com/t/ecs-development-status-december-2025/1699284))
- Growing demand for large-scale animation (strategy games, crowd simulation)

**Negative Indicators:**
- DOTS learning curve remains steep
- Unity layoffs affected animation team (Q1 2024)
- Adoption slower than originally projected
- Alternative engines (Godot) gaining share with simpler workflows

---

## 2. Competitive Landscape

### 2.1 Direct Competitors

#### **Rukhanka Animation System 2** (Primary Competitor)

| Attribute | Details |
|-----------|---------|
| **Price** | $120 (upgrade from v1: $30) |
| **Rating** | 5.0/5.0 (52 reviews) |
| **Users** | 376 favorites |
| **Key Features** | GPU/CPU animation, **Unity Animator Controller conversion**, all 2D blend trees, IK, root motion, full source code |
| **Differentiator** | **Already supports Animator conversion**, comprehensive feature set |
| **Weakness** | Proprietary, no community ecosystem |

[Asset Store Link](https://assetstore.unity.com/packages/tools/animation/rukhanka-animation-system-2-298480)

**Critical Insight:** Rukhanka already offers Unity Animator Controller conversion at $120. This is the primary competitive threat. However, reviews emphasize the creator's support quality over raw features.

#### **GPU ECS Animation Baker** (Partial Competitor)

| Attribute | Details |
|-----------|---------|
| **Price** | $53.90 |
| **Rating** | Good (26 reviews) |
| **Users** | 510 favorites |
| **Key Features** | GPU-based animation, crowd simulation, animator conversion |
| **Differentiator** | Lower price, GPU focus |
| **Weakness** | Less comprehensive, vertex animation focus |

[Asset Store Link](https://assetstore.unity.com/packages/tools/animation/gpu-ecs-animation-baker-250425)

### 2.2 Indirect Competitors / Alternatives

| Solution | Type | Notes |
|----------|------|-------|
| **Latios Framework (Kinemation)** | Free/Open Source | DMotion dependency; no state machine, low-level API |
| **Unity.Animation (Official)** | Experimental | Incomplete, not production-ready |
| **Custom Implementation** | DIY | High effort, common for large studios |
| **Hybrid Approach** | Workaround | GameObjects for animation, ECS for logic |

### 2.3 Competitive Positioning Matrix

```
                    Feature Completeness
                    Low ←───────────────→ High
           High ┌─────────────────────────────┐
                │                  Rukhanka   │
                │                    [$120]   │
         Price  │                             │
                │  GPU ECS Baker              │
                │     [$54]     Unity Bridge  │
                │               [PROPOSED]    │
           Low  │  Latios/Kinemation [FREE]   │
                └─────────────────────────────┘
```

### 2.4 Differentiation Opportunities

To compete with Rukhanka ($120), the Bridge must offer:

1. **Lower Price Point** — Undercut at $79-99
2. **Open Source Ecosystem** — DMotion community + contributions
3. **Migration Focus** — Better tooling for existing project conversion
4. **DMotion Integration** — Native state machine editor, not just conversion
5. **Documentation Quality** — Comprehensive migration guides
6. **Performance Claims** — Benchmark comparisons (DMotion is 6x faster than Mecanim)

---

## 3. Product Feature Analysis

### 3.1 Current Implementation Status (PR #38)

Based on [PR #38](https://github.com/gabrieldechichi/dmotion/pull/38):

| Metric | Value |
|--------|-------|
| **Commits** | 47 |
| **Files Changed** | 188 |
| **Lines Added** | +25,898 |
| **Tests** | 40+ (unit, editor, integration) |
| **Feature Parity** | 32% with Unity Animator |

#### Implemented Features:

| Feature | Status | Native DMotion Support |
|---------|--------|----------------------|
| Single Clip States | ✅ Complete | Yes |
| Parameters (Float/Int/Bool) | ✅ Complete | Yes |
| Trigger Parameters | ✅ Complete | Yes |
| Transitions with Conditions | ✅ Complete | Yes |
| Exit Time Transitions | ✅ Complete | Yes |
| 1D Blend Trees | ✅ Complete | Yes |
| Any State Transitions | ✅ Complete | Native (100× smaller, 50% faster) |
| Sub-State Machines | ✅ Complete | Native (unlimited depth) |
| Speed Parameters | ✅ Complete | Native |
| Animation Events | ✅ Complete | Converted |
| Conversion Reports | ✅ Complete | — |

#### Not Yet Implemented:

| Feature | Priority | Complexity |
|---------|----------|------------|
| 2D Blend Trees (all types) | **HIGH** | Medium |
| Multiple Layers | **HIGH** | High |
| Avatar Masks | Medium | Medium |
| Root Motion | Medium | Medium |
| IK Integration | Low | High |
| StateMachineBehaviour | Not Planned | N/A (managed code) |

### 3.2 Comparison with Rukhanka

| Feature | DMotion Bridge | Rukhanka |
|---------|---------------|----------|
| Unity Animator Conversion | ✅ | ✅ |
| 1D Blend Trees | ✅ | ✅ |
| **2D Blend Trees** | ❌ | ✅ |
| **Multiple Layers** | ❌ | ✅ |
| Any State | ✅ (Native) | ✅ |
| Sub-State Machines | ✅ (Native) | ? |
| Speed Parameters | ✅ (Native) | ✅ |
| GPU Animation | ❌ (via Kinemation) | ✅ |
| IK | ❌ | ✅ |
| Root Motion | ❌ | ✅ |
| Netcode Integration | ❌ | ✅ |
| Source Code | Partial (bridge closed) | ✅ Full |
| Open Source Core | ✅ DMotion | ❌ |
| Price | TBD | $120 |

**Gap Analysis:** Missing 2D blend trees and multiple layers are critical for parity with Rukhanka. Without these, the product targets a subset of users (simpler animation needs).

### 3.3 Unique Value Propositions

1. **Open Source Core** — DMotion remains free; only bridge is paid
2. **Community Ecosystem** — Contributions improve base library
3. **Native DOTS Patterns** — Features implemented as first-class DMotion concepts, not workarounds
4. **Performance** — DMotion benchmarks at 6x faster than Mecanim
5. **Architectural Cleanliness** — "Bridge as translator, not workaround generator"
6. **Test Coverage** — 40+ comprehensive tests

---

## 4. Pricing Strategy

### 4.1 Market Benchmarks

| Product | Price | Positioning |
|---------|-------|-------------|
| Rukhanka Animation System 2 | $120 | Premium, full-featured |
| GPU ECS Animation Baker | $54 | Mid-range, specialized |
| Top Animation Assets (avg) | $50-150 | Varies |
| Tools/Editor Extensions (median) | $30-80 | Common range |

### 4.2 Recommended Pricing Tiers

#### Option A: Single Price (Simple)

**$79** — Undercuts Rukhanka by ~35% while maintaining perceived value

Pros:
- Simple purchase decision
- Competitive positioning
- Room for sale pricing ($49-59)

Cons:
- Leaves money on table for enterprise
- May signal "less than Rukhanka"

#### Option B: Tiered Pricing (Recommended)

| Tier | Price | Target | Includes |
|------|-------|--------|----------|
| **Indie** | $49 | Solo developers, <$100K revenue | Full bridge, 1 seat |
| **Pro** | $99 | Teams, commercial projects | Full bridge, 3 seats, priority support |
| **Enterprise** | $299 | Studios, >$500K revenue | Unlimited seats, source access, dedicated support |

Pros:
- Captures value across segments
- Indie tier drives adoption
- Enterprise tier captures high-value customers

Cons:
- Revenue tracking complexity
- License enforcement needed

### 4.3 Revenue Projections

**Assumptions:**
- Market size: 35,000 addressable developers
- Conversion rate: 1-3% (conservative for niche tools)
- Price: $79 average

| Scenario | Conversion | Sales | Gross Revenue | Net (after 30% cut) |
|----------|------------|-------|---------------|---------------------|
| Pessimistic | 0.5% | 175 | $13,825 | $9,678 |
| Conservative | 1% | 350 | $27,650 | $19,355 |
| Moderate | 2% | 700 | $55,300 | $38,710 |
| Optimistic | 3% | 1,050 | $82,950 | $58,065 |

**First Year Target:** 300-500 sales = $20,000-$35,000 net revenue

### 4.4 Price Elasticity Considerations

- **Below $50:** May signal low quality; race-to-bottom risk
- **$50-80:** Sweet spot for indie/hobbyist adoption
- **$80-120:** Requires feature parity with Rukhanka
- **Above $120:** Must clearly exceed Rukhanka in value

---

## 5. Legal Considerations

### 5.1 Recommended Licensing Model: Separate Works

The recommended approach treats DMotion and the Bridge as **separate works with independent licenses**:

```
┌─────────────────────────────────────────────┐
│  DMotion Core                               │
│  License: Unity Companion License           │
│  Status: Open source, unchanged             │
│  Relationship: Dependency (not modified)    │
└─────────────────────────────────────────────┘
                      │
                      │ Public API calls only
                      ▼
┌─────────────────────────────────────────────┐
│  Unity Controller Bridge                    │
│  License: Proprietary / Commercial EULA     │
│  Status: Closed source, 100% owned by you   │
│  Relationship: Interoperating work          │
└─────────────────────────────────────────────┘
```

### 5.2 Legal Basis: Derivative vs. Interoperating Works

The Bridge qualifies as an **interoperating work**, not a **derivative work**:

| Derivative Work | Interoperating Work (Bridge) |
|-----------------|------------------------------|
| Modifies original source code | Uses public API only |
| Copies substantial original code | Contains 100% original code |
| Cannot exist without embedding original | Standalone package requiring dependency |
| Subject to original license terms | Your own copyright and license |

**Key Facts Supporting Separate Licensing:**
- Bridge contains 25,000+ lines of original code (PR #38)
- Bridge calls DMotion's public baking/runtime APIs
- Bridge does not modify or redistribute DMotion source
- DMotion remains a separate package dependency

### 5.3 Industry Precedents

This model is standard practice in software:

| Base Software | Base License | Commercial Extension |
|---------------|--------------|---------------------|
| WordPress | GPL | Premium plugins (proprietary) |
| Unity Engine | Proprietary | Asset Store packages (seller's license) |
| Elasticsearch | Apache/SSPL | Commercial plugins |
| Linux Kernel | GPL | Proprietary drivers (API boundary) |
| MySQL | GPL | Enterprise features (proprietary) |

### 5.4 DMotion License Review

DMotion uses the **Unity Companion License**:

[Full License](https://unity.com/legal/licenses/unity-companion-license)

**Relevant Clauses:**

1. **Unity Engine Required** — Must be used "in connection with" valid Unity Engine License
   - ✅ Bridge users must have Unity — satisfied automatically

2. **No Competitive Use** — Cannot use for "competitive analysis or to develop a competing product"
   - ✅ Bridge extends DMotion, doesn't compete — drives adoption

3. **Derivative Works Assignment** — Derivatives of the Software assigned to Unity
   - ✅ Bridge is interoperating work, not derivative — your IP

### 5.5 Requirements for Clean Separation

To maintain legal separation, the Bridge must:

| Requirement | Implementation |
|-------------|----------------|
| **Clean API boundary** | Only use DMotion's public types/methods |
| **No code copying** | Never paste DMotion code into Bridge |
| **Separate distribution** | Bridge via Asset Store, DMotion via UPM |
| **Clear dependency** | Document "Requires DMotion (free)" |
| **Copyright documentation** | Maintain authorship records for all Bridge code |
| **No DMotion modification** | Fork contributions stay open source |

### 5.6 Recommended Bridge License

**Option A: Unity Asset Store EULA (Recommended)**
- Automatically applied when sold on Asset Store
- Handles licensing, refunds, distribution
- Industry-standard, trusted by buyers

**Option B: Custom Commercial EULA**
- Single/team seat licensing tiers
- No redistribution clause
- No reverse engineering clause
- Required for non-Asset Store distribution

### 5.7 Risk Assessment (Updated)

| Question | Assessment | Risk Level |
|----------|------------|------------|
| Is the Bridge a "derivative work"? | No — interoperating work using public API | **LOW** |
| Can you sell closed-source Bridge? | Yes — standard plugin/extension model | **LOW** |
| Does this compete with DMotion? | No — extends and drives adoption | **LOW** |
| Does this compete with Unity? | No — Unity has no equivalent product | **LOW** |
| Could licensing be challenged? | Unlikely but possible | **LOW-MEDIUM** |

### 5.8 Recommended Legal Actions

1. **30-minute IP attorney consultation** — Confirm interoperating work status ($150-300)
2. **Document code authorship** — Git history, copyright headers in all Bridge files
3. **Maintain clean separation** — Never merge DMotion code into Bridge
4. **Use Asset Store EULA** — Leverage established legal framework
5. **Optional: Notify DMotion author** — Courtesy, not legally required

---

## 6. Risk Assessment

### 6.1 Risk Matrix

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| **Legal/Licensing Issues** | Low | Medium | Separate works model, brief attorney consultation |
| **Rukhanka captures market** | High | Medium | Feature parity, lower price, community |
| **DOTS adoption stalls** | Medium | High | Diversify to other markets |
| **Unity changes direction** | Low | High | Monitor Unity roadmap |
| **Support burden** | High | Medium | Documentation, community forums |
| **Feature requests exceed capacity** | High | Medium | Clear roadmap, staged releases |
| **Price sensitivity** | Medium | Medium | Tiered pricing, sales events |

### 6.2 Competitor Response Scenarios

**If Rukhanka lowers price:**
- They're established with 52 reviews
- Price war unlikely to favor newcomer
- Differentiate on open-source ecosystem

**If Unity releases official solution:**
- Long timeline (years) based on current roadmap
- Community tools typically more feature-rich
- Opportunity window exists

**If DMotion author objects:**
- Negotiate licensing terms
- Worst case: pivot to clean-room implementation

---

## 7. Recommendations

### 7.1 Go/No-Go Decision Framework

| Factor | Status | Weight |
|--------|--------|--------|
| Market demand exists | ✅ Yes | High |
| Technical implementation viable | ✅ Yes (32% parity, clear roadmap) | High |
| Competition manageable | ⚠️ Rukhanka is strong | Medium |
| Legal path clear | ✅ Yes (separate works model) | High |
| Revenue potential sufficient | ⚠️ Moderate ($20-60K Y1) | Medium |

**Recommendation: GO**

Proceed with:
1. Brief attorney consultation to confirm separate works model (~$150-300)
2. 2D Blend Trees implementation (feature parity baseline)
3. Multiple Layers implementation (enterprise requirement)

### 7.2 Minimum Viable Product (MVP) for Launch

| Feature | Priority | Status |
|---------|----------|--------|
| Single Clip States | Required | ✅ |
| Parameters (all types) | Required | ✅ |
| Transitions | Required | ✅ |
| 1D Blend Trees | Required | ✅ |
| Any State | Required | ✅ |
| Sub-State Machines | Required | ✅ |
| **2D Blend Trees** | Required | ❌ Must implement |
| **Multiple Layers** | Required | ❌ Must implement |
| Conversion Reports | Recommended | ✅ |
| Documentation | Required | ✅ |

### 7.3 Suggested Roadmap

**Phase 1: Legal & Setup (1 week)**
- Brief IP attorney consultation (~$150-300)
- Add copyright headers to all Bridge source files
- Set up Asset Store publisher account

**Phase 2: Feature Completion (4-8 weeks)**
- Implement 2D Blend Trees (all types)
- Implement Multiple Layers
- Performance optimization
- Documentation completion

**Phase 3: Beta Testing (2-4 weeks)**
- Recruit 10-20 beta testers from DOTS community
- Gather feedback, fix critical bugs
- Refine documentation

**Phase 4: Launch Preparation (2 weeks)**
- Unity Asset Store submission
- Marketing materials (videos, blog posts)
- Support infrastructure

**Phase 5: Launch**
- Asset Store release
- Community announcement (Unity Forums, Reddit, Discord)
- Free trial or limited version consideration

### 7.4 Marketing Strategy

**Target Channels:**
1. Unity Asset Store — primary distribution
2. Unity Forums — [DOTS Animation thread](https://discussions.unity.com/t/dots-animation-options-wiki/894948)
3. Reddit — r/Unity3D, r/gamedev
4. Discord — Unity DOTS servers
5. YouTube — Migration tutorials, comparisons

**Key Messages:**
- "Migrate your existing AnimatorControllers to DOTS in minutes"
- "6x faster than Mecanim, with the workflow you know"
- "Open-source foundation, professional bridge"

**Content Strategy:**
- Blog series: "Migrating from Mecanim to DMotion"
- Video: Before/After performance comparison
- Case study: Migration of sample project

### 7.5 Alternative Business Models

If direct sales prove insufficient:

| Model | Description | Pros | Cons |
|-------|-------------|------|------|
| **Freemium** | Basic conversion free, advanced features paid | Adoption, evangelism | Complex to split features |
| **Subscription** | $10-15/month | Recurring revenue | Poor fit for Unity culture |
| **Consulting** | Paid migration services | High value per customer | Doesn't scale |
| **Donation/Sponsor** | Open source, GitHub sponsors | Community goodwill | Unreliable income |
| **Dual License** | MIT for open source, paid for commercial | Legal clarity | Enforcement difficulty |

---

## 8. Sources

### Market Research

- [Unity DOTS Official Page](https://unity.com/dots)
- [DOTS Development Status - December 2025](https://discussions.unity.com/t/ecs-development-status-december-2025/1699284)
- [VG Insights Game Engines Report 2025](https://vginsights.com/assets/reports/The_Big_Game_Engines_Report_of_2025.pdf)
- [6sense Unity Market Share](https://6sense.com/tech/game-development/unity-market-share)
- [SlashData Game Engine Survey](https://www.slashdata.co/post/did-you-know-that-60-of-game-developers-use-game-engines)

### Competitor Analysis

- [Rukhanka Animation System 2 - Asset Store](https://assetstore.unity.com/packages/tools/animation/rukhanka-animation-system-2-298480)
- [Rukhanka Documentation](https://docs.rukhanka.com/)
- [GPU ECS Animation Baker - Asset Store](https://assetstore.unity.com/packages/tools/animation/gpu-ecs-animation-baker-250425)
- [Latios Framework GitHub](https://github.com/Dreaming381/Latios-Framework)
- [DMotion GitHub](https://github.com/gabrieldechichi/dmotion)

### Community & Pain Points

- [DOTS Animation Options Wiki](https://discussions.unity.com/t/dots-animation-options-wiki/894948)
- [Animation Status Update Q1 2024](https://discussions.unity.com/t/animation-status-update-q1-2024/942037)
- [Animation workaround in DOTS 1.0](https://discussions.unity.com/t/animation-workaround-in-dots-1-0/897023)
- [How to do Animations in ECS/DOTS](https://discussions.unity.com/t/question-how-to-do-animations-in-ecs-dots-or-best-workarounds/810745)

### Asset Store Economics

- [Unity Asset Store Revenue Documentation](https://docs.unity3d.com/2022.3/Documentation/Manual/asset-store-revenue.html)
- [How Profitable Is Selling Unity Assets](https://mktclarity.com/blogs/news/unity-assets-profitable)
- [Top Unity Stores Making Money](https://mktclarity.com/blogs/news/top-unity-stores)

### Legal

- [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license)
- [Unity Companion License Discussion](https://forum.unity.com/threads/unity-companion-license-being-a-roadblock-to-open-source.542608/)

### Implementation Reference

- [DMotion PR #38 - Unity Controller Bridge](https://github.com/gabrieldechichi/dmotion/pull/38)
- [DMotion Unity Forum Thread](https://discussions.unity.com/t/0-4-0-dmotion-a-high-level-animation-framework-for-dots/889454)

---

## Appendix A: Feature Parity Checklist

### Unity Animator Features vs. Bridge Support

| Unity Feature | Bridge Status | Priority |
|--------------|---------------|----------|
| **States** | | |
| Single Clip State | ✅ Supported | — |
| Speed Parameter | ✅ Supported (Native) | — |
| Motion Time Parameter | ❌ Not Supported | Low |
| Cycle Offset | ❌ Not Supported | Low |
| Mirror | ❌ Not Supported | Low |
| **Blend Trees** | | |
| 1D | ✅ Supported | — |
| 2D Simple Directional | ❌ Not Supported | **High** |
| 2D Freeform Directional | ❌ Not Supported | **High** |
| 2D Freeform Cartesian | ❌ Not Supported | **High** |
| Direct | ❌ Not Supported | Medium |
| Nested Blend Trees | ❌ Not Supported | Medium |
| **Transitions** | | |
| Condition-based | ✅ Supported | — |
| Exit Time | ✅ Supported | — |
| Fixed Duration | ✅ Supported | — |
| Interruption Source | ❌ Not Supported | Low |
| Ordered Interruption | ❌ Not Supported | Low |
| **Parameters** | | |
| Float | ✅ Supported | — |
| Int | ✅ Supported | — |
| Bool | ✅ Supported | — |
| Trigger | ✅ Supported | — |
| **Organization** | | |
| Sub-State Machines | ✅ Supported (Native) | — |
| Any State | ✅ Supported (Native) | — |
| Entry/Exit Nodes | ✅ Supported | — |
| **Layers** | | |
| Multiple Layers | ❌ Not Supported | **High** |
| Override Mode | ❌ Not Supported | **High** |
| Additive Mode | ❌ Not Supported | Medium |
| Layer Sync | ❌ Not Supported | Low |
| Avatar Masks | ❌ Not Supported | Medium |
| **Advanced** | | |
| Root Motion | ❌ Not Supported | Medium |
| IK Pass | ❌ Not Supported | Low |
| Animation Events | ✅ Converted | — |
| StateMachineBehaviour | ❌ Not Planned | — |
| Animator Override Controller | ❌ Not Supported | Medium |

**Current Parity: ~32%**
**Post-MVP Parity (with 2D/Layers): ~60%**
**Roadmap Target: ~70%**

---

## Appendix B: Competitor Feature Comparison

| Feature | DMotion Bridge | Rukhanka | GPU ECS Baker |
|---------|---------------|----------|---------------|
| **Price** | TBD ($49-99) | $120 | $54 |
| **Animator Conversion** | ✅ | ✅ | ✅ |
| **1D Blend Trees** | ✅ | ✅ | ✅ |
| **2D Blend Trees** | ❌ | ✅ | ❌ |
| **Multiple Layers** | ❌ | ✅ | ❌ |
| **Any State** | ✅ Native | ✅ | ? |
| **Sub-State Machines** | ✅ Native | ? | ❌ |
| **GPU Animation** | ❌ | ✅ | ✅ |
| **IK** | ❌ | ✅ | ❌ |
| **Root Motion** | ❌ | ✅ | ❌ |
| **Netcode** | ❌ | ✅ | ❌ |
| **Source Code** | Partial | Full | Partial |
| **Open Source Core** | ✅ | ❌ | ❌ |
| **Test Coverage** | 40+ tests | ? | ? |
| **Performance** | 6x Mecanim | ? | GPU-optimized |
| **Reviews** | N/A (new) | 52 (5.0★) | 26 |

---

*Document generated: 2026-01-15*
*Analysis based on publicly available information and PR #38 implementation status*
