# DMotion Implementation Roadmap

## Context

**Project Status**: DMotion forked (original abandoned 2023), full control for implementation
**Goal**: Implement native animation features, eliminate bridge workarounds
**Strategy**: DMotion-First approach - native solutions over bridge hacks

---

## Phase 1: Native Any State Support (Priority 1) 🔥

**Status**: Ready to implement
**Timeline**: 1-2 weeks
**Impact**: Eliminates Phase 12.4 workaround, 100× asset size reduction, 50% faster

### Week 1: Core Implementation

**Day 1-2: Data Structures**
- [ ] Add `AnyStateTransition` struct to `Runtime/Assets/StateMachineAsset.cs`
- [ ] Add `AnyStateTransitions` BlobArray to `StateMachineAsset`
- [ ] Unit test: Struct serialization

**Day 3-4: Runtime Evaluation**
- [ ] Find transition evaluation system (likely in `Runtime/Systems/`)
- [ ] Add Any State evaluation loop BEFORE regular transitions
- [ ] Handle self-transitions (state → itself)
- [ ] Unit tests: Evaluation order, first-match, self-transition

**Day 5: Authoring Support**
- [ ] Create `AnyStateTransitionAuthoring` class
- [ ] Update `StateMachineAuthoring` with `AnyStateTransitions` list
- [ ] Custom Inspector with validation warnings
- [ ] Editor tests: Authoring serialization

### Week 2: Bridge & Polish

**Day 1-2: Baking**
- [ ] Update baker to convert authoring → runtime
- [ ] State name resolution
- [ ] Exit time conversion (normalized → absolute)
- [ ] Integration tests: Authoring → Runtime pipeline

**Day 3: Bridge Translation**
- [ ] Add `AnyStateTransitions` list to `StateMachineData` (bridge POCO)
- [ ] Update `UnityControllerAdapter.ReadStateMachine()` for pure translation
- [ ] **DELETE** `ExpandAnyStateTransitions()` workaround method
- [ ] Update conversion engine to bake Any State
- [ ] Integration tests: Unity → DMotion conversion

**Day 4: Testing & Validation**
- [ ] Performance tests: Native vs workaround (expect ~50% faster)
- [ ] Asset size tests: Native vs workaround (expect ~100× smaller)
- [ ] Unity parity tests: Compare behavior with Unity sample controllers
- [ ] Create test scene with Any State examples

**Day 5: Documentation**
- [ ] Update `AnyStateTransitions.md` (DMotion user guide)
- [ ] Update `UnityControllerBridge_AnyStateGuide.md` (add native support section)
- [ ] Create migration guide from workaround to native
- [ ] Update `CHANGELOG.md` with breaking changes

### Deliverables
- ✅ Native Any State support in DMotion runtime
- ✅ Authoring tools for manual DMotion users
- ✅ Pure bridge translation (workaround deleted)
- ✅ Performance benchmarks showing improvement
- ✅ Complete documentation and migration guide

---

## Phase 2: Hierarchical State Machines (Priority 2) 🔥

**Status**: After Any State complete
**Timeline**: 2-3 weeks
**Impact**: Eliminates Phase 12.5 workaround, preserves structure

### Week 1: Core Implementation

**Day 1-3: Data Structures**
- [ ] Add `SubStateMachineAsset` struct with recursive structure
- [ ] Add `SubMachines` BlobArray to `StateMachineAsset`
- [ ] Update `TransitionDestinationType` enum (State, SubMachine, Exit)
- [ ] Update `StateOutTransition` with destination discriminator
- [ ] Unit tests: Nested structure, depth limits

**Day 4-5: Runtime State Management**
- [ ] Add `ActiveStateMachine` component with sub-machine stack
- [ ] Implement stack push/pop for sub-machine navigation
- [ ] Update transition system to handle Entry/Exit
- [ ] Unit tests: Navigation, stack management

### Week 2: Entry/Exit & Evaluation

**Day 1-2: Entry/Exit Logic**
- [ ] Implement Entry: Transition to sub-machine's default state
- [ ] Implement Exit: Pop stack, resolve parent transitions
- [ ] Handle nested Entry/Exit (2-3 levels deep)
- [ ] Unit tests: Entry/Exit behavior

**Day 3-4: Any State Scoping**
- [ ] Any State transitions scoped to current sub-machine
- [ ] Evaluate parent Any State if no local match
- [ ] Unit tests: Any State hierarchy

**Day 5: Authoring Support**
- [ ] Create `SubStateMachineAuthoring` class
- [ ] Recursive authoring structure
- [ ] Inspector visualization (foldouts for hierarchy)
- [ ] Editor tests

### Week 3: Bridge & Polish

**Day 1-2: Baking**
- [ ] Recursive baker for sub-machines
- [ ] Resolve Entry/Exit transitions
- [ ] Integration tests

**Day 2-3: Bridge Translation**
- [ ] Add recursive `ConvertSubMachine()` method
- [ ] **DELETE** flattening workaround (Phase 12.5 cancelled)
- [ ] Preserve hierarchy, no name prefixing
- [ ] Integration tests: Unity sub-machines → DMotion

**Day 4-5: Testing & Documentation**
- [ ] Complex hierarchy tests (3+ levels)
- [ ] Unity parity tests
- [ ] Documentation: Sub-machine user guide
- [ ] Migration guide (no flattening needed!)

### Deliverables
- ✅ Native hierarchical state machines in DMotion
- ✅ Entry/Exit node support
- ✅ Pure bridge translation (no flattening)
- ✅ Complete documentation

---

## Phase 3: Trigger Auto-Reset (Priority 3) ⚡

**Status**: After Any State complete
**Timeline**: 1 week
**Impact**: Unity parity, no manual reset needed

### Week 1: Implementation

**Day 1-2: Data Structures**
- [ ] Add `TriggerParameter` buffer component
- [ ] Add `ConsumedThisFrame` tracking flag
- [ ] Update parameter evaluation to mark consumed
- [ ] Unit tests: Trigger marking

**Day 3: Reset System**
- [ ] Create `TriggerResetSystem` (runs after transition evaluation)
- [ ] Reset consumed triggers to false
- [ ] Unit tests: Auto-reset behavior

**Day 4: Authoring & Baking**
- [ ] Add Trigger type to parameter authoring
- [ ] Update baker
- [ ] Integration tests

**Day 5: Bridge & Documentation**
- [ ] Update bridge to map Trigger → Trigger (not Bool)
- [ ] Remove warning about manual reset
- [ ] Documentation: Trigger behavior guide
- [ ] Migration: No more manual reset needed!

### Deliverables
- ✅ Native Trigger parameter type
- ✅ Auto-reset behavior (Unity parity)
- ✅ Bridge translation updated
- ✅ Documentation

---

## Phase 4: Float Conditions (Priority 4) ⚡

**Status**: After Triggers
**Timeline**: 3-5 days
**Impact**: Controllers with float conditions work

### Implementation

**Day 1: Data Structures**
- [ ] Add `FloatConditionComparison` enum (Greater, Less)
- [ ] Update `TransitionCondition` union with float fields
- [ ] Unit tests

**Day 2: Runtime Evaluation**
- [ ] Add float condition evaluation to transition system
- [ ] Unit tests: Float comparisons

**Day 3: Authoring & Baking**
- [ ] Update condition authoring
- [ ] Update baker
- [ ] Integration tests

**Day 4-5: Bridge & Documentation**
- [ ] Bridge translation for float conditions
- [ ] Documentation
- [ ] Unity parity tests

### Deliverables
- ✅ Float condition support
- ✅ Greater/Less comparisons
- ✅ Bridge translation

---

## Phase 5: Speed Parameters (Priority 5) ⚡

**Timeline**: 1 week
**Impact**: Dynamic speed control

### Implementation

**Day 1-2: Data Structures**
- [ ] Add `SpeedParameterHash` to `AnimationStateAsset`
- [ ] Add `UseSpeedParameter` flag
- [ ] Unit tests

**Day 3-4: Runtime**
- [ ] Update animation playback to multiply speeds
- [ ] `finalSpeed = state.Speed * GetFloatParameter(hash)`
- [ ] Unit tests: Speed multiplication

**Day 5: Authoring, Bridge, Documentation**
- [ ] Authoring support
- [ ] Bridge translation
- [ ] Documentation

### Deliverables
- ✅ Speed parameter support
- ✅ Dynamic speed control

---

## Phase 6: Offsets (Priority 6) ⚡

**Timeline**: 2-3 days
**Impact**: Animation synchronization

### Implementation

**Day 1: Cycle Offset**
- [ ] Add `CycleOffset` to `AnimationStateAsset`
- [ ] Apply on state entry
- [ ] Tests

**Day 2: Transition Offset**
- [ ] Add `DestinationOffset` to `StateOutTransition`
- [ ] Apply on transition entry
- [ ] Tests

**Day 3: Bridge & Documentation**
- [ ] Bridge translation
- [ ] Documentation

### Deliverables
- ✅ Cycle offset support
- ✅ Transition offset support

---

## Phase 7: 2D Blend Trees (Priority 7 - Long-term) 🔮

**Status**: After Phases 1-6 complete
**Timeline**: 2-3 months
**Impact**: Industry-standard locomotion

### Planning Required
- Research Unity's 2D blend tree algorithms
- Design DMotion asset structures
- Plan runtime blending implementation
- Consider Burst compatibility

**Defer until**: Core features (Any State, Sub-machines) complete

---

## Phase 8: Multiple Layers (Priority 8 - Long-term) 🔮

**Status**: After 2D Blend Trees
**Timeline**: 3-4 months
**Impact**: AAA animation workflows

### Major Architecture Change
- Multi-layer state machine support
- Avatar masking system
- Layer blending (Override, Additive)
- Significant runtime refactor

**Defer until**: 2D blend trees complete

---

## Implementation Order (Recommended)

### Sprint 1 (Weeks 1-2): Any State ✅ PRIORITY
**Why first**: Highest impact, eliminates existing workaround, enables better architecture

### Sprint 2 (Weeks 3-5): Hierarchical State Machines
**Why next**: Prevents Phase 12.5 workaround, structural foundation for complex controllers

### Sprint 3 (Week 6): Triggers
**Why next**: Quick win, fixes behavioral difference with Unity

### Sprint 4 (Week 7): Float Conditions
**Why next**: Quick win, expands condition support

### Sprint 5 (Week 8): Speed Parameters
**Why next**: High-value feature, commonly requested

### Sprint 6 (Week 9): Offsets
**Why next**: Polish feature, animation synchronization

### Future Sprints: 2D Blend Trees, Layers
**Why later**: Complex, require significant research and design

---

## Success Metrics

### Phase 1 (Any State) Success Criteria
- [ ] Any State transitions work identically to Unity
- [ ] ~50% faster evaluation than workaround
- [ ] ~100× smaller asset size than workaround
- [ ] 100% test coverage for Any State code paths
- [ ] Bridge workaround deleted (code reduction)
- [ ] Migration guide complete

### Overall Project Success (After Phase 6)
- [ ] Feature parity: ~70% of Unity AnimatorController features
- [ ] Bridge complexity: Low (pure translation layer)
- [ ] Performance: Better or equal to Unity
- [ ] Documentation: Complete and comprehensive
- [ ] No outstanding workarounds in bridge

---

## Development Setup

### Branching Strategy
```bash
main                    # Stable releases
├── develop            # Integration branch
├── feature/any-state  # Phase 1
├── feature/sub-machines  # Phase 2
├── feature/triggers   # Phase 3
└── feature/float-conditions  # Phase 4
```

### Feature Branch Workflow
1. Create feature branch from `develop`
2. Implement with tests
3. PR to `develop` with documentation
4. Review + merge
5. Delete feature branch

### Testing Requirements
Every feature must have:
- Unit tests (Runtime behavior)
- Integration tests (Authoring → Runtime)
- Bridge tests (Unity → DMotion conversion)
- Performance tests (benchmarks)

---

## Next Immediate Steps

### TODAY: Kick off Any State Implementation
```bash
# 1. Create feature branch
git checkout -b feature/native-any-state-support

# 2. Start with data structures
# Edit: Runtime/Assets/StateMachineAsset.cs
# Add: AnyStateTransition struct
# Add: BlobArray<AnyStateTransition> AnyStateTransitions

# 3. Write initial tests
# Create: Tests/Runtime/AnyStateTransitionTests.cs
# Test: Struct serialization, BlobArray allocation

# 4. Commit progress
git add .
git commit -m "Add AnyStateTransition data structures"
```

### THIS WEEK: Complete Any State Core
- Day 1-2: Data structures + tests ✅
- Day 3-4: Runtime evaluation + tests
- Day 5: Authoring support + tests
- Weekend: Review progress

### NEXT WEEK: Complete Any State Bridge
- Day 1-2: Baking + tests
- Day 3: Bridge translation + tests
- Day 4: Performance tests + benchmarks
- Day 5: Documentation + migration guide

### WEEK 3: Phase 2 Planning
- Review Any State implementation
- Plan hierarchical state machines
- Start Phase 2 data structures

---

## Questions & Decisions

### Backward Compatibility
**Decision needed**: Support old assets without AnyStateTransitions?
**Recommendation**: Yes, empty array defaults for old assets

### API Stability
**Decision needed**: Can we make breaking changes?
**Recommendation**: Yes, but document in changelog with migration guide

### Performance Targets
**Decision needed**: What are acceptable performance benchmarks?
**Recommendation**: Native ≥ Unity performance for all features

### Code Style
**Decision needed**: Follow existing DMotion conventions?
**Recommendation**: Yes, maintain consistency with existing codebase

---

## Risk Management

### Risk: Complex Runtime Integration
**Mitigation**: Start with unit tests for isolated components, then integration

### Risk: Unity Parity Issues
**Mitigation**: Test against real Unity sample projects, verify behavior matches

### Risk: Performance Regressions
**Mitigation**: Benchmarks for every feature, performance tests in CI

### Risk: Breaking Existing Users
**Mitigation**: Backward compatibility, migration guides, deprecation warnings

---

## Communication

### Changelog Format
```markdown
## [Version X.X.X] - Date

### Added
- Native Any State transition support
- AnyStateTransition struct and runtime evaluation
- Bridge translation for Any State (1:1 mapping)

### Changed
- StateMachineAsset now includes AnyStateTransitions array

### Deprecated
- Any State expansion workaround (Phase 12.4) - use native support

### Performance
- 50% faster Any State evaluation vs workaround
- 100× smaller assets for typical state machines (100 states, 3 Any State)

### Migration
See Migration_AnyStateWorkaroundToNative.md for upgrade guide
```

---

## Resources

### Documentation Created
- [x] Architecture Analysis
- [x] DMotion-First Plan
- [x] Implementation Guide (Any State)
- [x] This Roadmap

### Implementation References
- `Implementation_AnyStateNative.md` - Detailed Any State guide
- `UnityControllerBridge_DMotionFirst_Plan.md` - Overall strategy
- `UnityControllerBridge_ArchitectureAnalysis.md` - Why native is better

### Existing Codebase References
- Study `StateMachineAsset` structure
- Find transition evaluation system
- Review existing baking pipeline
- Check parameter evaluation patterns

---

## Let's Begin! 🚀

**First task**: Add `AnyStateTransition` struct to `Runtime/Assets/StateMachineAsset.cs`

Ready to start implementation?
