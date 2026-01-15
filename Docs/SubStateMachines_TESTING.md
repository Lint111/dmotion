# Sub-State Machines - Testing & Validation Guide

## Quick Smoke Test (5-10 minutes)

### Test 1: Bridge Conversion with Sub-State Machine
**Goal**: Verify Unity AnimatorController with sub-state machine converts successfully

**Steps**:
1. Create a simple Unity AnimatorController with:
   - Root states: Idle, Walk
   - Sub-state machine: "Combat" containing:
     - Attack
     - Block
     - Dodge
   - Transition: Walk → Combat (on bool parameter "InCombat")
   - Entry state in Combat: Attack

2. Create UnityControllerBridgeAsset:
   ```
   Assets > Create > DMotion > Unity Controller Bridge
   ```

3. Assign the AnimatorController and click "Convert"

4. **Expected Results**:
   - ✅ Console shows: "Converted 1 sub-state machine(s) (native DMotion support)"
   - ✅ Generated StateMachineAsset contains SubStateMachineStateAsset
   - ✅ No errors in console

### Test 2: Manual Authoring
**Goal**: Verify manual creation of SubStateMachineStateAsset works

**Steps**:
1. Create two StateMachineAssets manually
2. In the parent machine, add SubStateMachineStateAsset to States list
3. Assign nested machine and entry state

4. **Expected Results**:
   - ✅ Inspector shows SubStateMachineStateAsset fields
   - ✅ NestedStateMachine and EntryState can be assigned
   - ✅ IsValid() returns true in inspector

### Test 3: Runtime Baking
**Goal**: Verify baking pipeline handles sub-state machines

**Steps**:
1. Add AnimationStateMachineAuthoring component to GameObject
2. Assign StateMachine with sub-state machine (from Test 1 or 2)
3. Enter Play Mode (triggers baking)

4. **Expected Results**:
   - ✅ No baking errors
   - ✅ Entity has StateMachineContext buffer component
   - ✅ StateMachineBlob contains SubStateMachines array
   - ✅ Console shows: "Built N Any State transition(s)" if applicable

### Test 4: Runtime Evaluation
**Goal**: Verify hierarchical state machine actually runs

**Steps**:
1. Use GameObject from Test 3
2. Set bool parameter to trigger transition into sub-state machine
3. Watch entity in Entities Hierarchy window

4. **Expected Results**:
   - ✅ StateMachineContext buffer grows when entering sub-machine
   - ✅ Animation plays from entry state of sub-machine
   - ✅ No runtime errors or exceptions

---

## Edge Case Testing (Optional)

### Test 5: Deep Nesting (3+ levels)
**Setup**: Create hierarchy: Root → SubA → SubB → SubC
**Expected**: ✅ Handles unlimited depth without errors

### Test 6: Circular Reference
**Setup**: SubA.NestedMachine references SubA itself
**Expected**: ⚠️ Validation error or infinite recursion caught

### Test 7: Empty Sub-Machine
**Setup**: Sub-machine with no states
**Expected**: ✅ Baking warning, graceful fallback

### Test 8: Exit Transitions
**Setup**: Sub-machine with exit transitions to parent states
**Expected**: ✅ Exits sub-machine when conditions met (NOT YET IMPLEMENTED)

---

## Performance Testing (Optional)

### Test 9: Large Hierarchy
**Setup**:
- Root machine with 10 states
- 5 sub-machines, each with 10 states
- 3 levels deep

**Metrics**:
- Baking time: < 1 second
- Runtime update: < 0.1ms per entity
- Memory: Check StateMachineContext buffer size

---

## Known Limitations (Current Implementation)

1. **Exit Transitions**: Not yet implemented in runtime evaluation
   - ExitSubStateMachine() exists but not called
   - Sub-machines can enter but not exit via "Up" transitions

2. **Visual Editor**: May not render sub-state machine nodes properly
   - Basic display should work
   - May lack specialized visualization

3. **Debugging**: No hierarchical breadcrumb in inspector yet
   - Can't see "Root > Combat > Attack" path easily

---

## Troubleshooting

### Error: "SubStateMachine states should be entered via EnterSubStateMachine"
- **Cause**: Bug in transition handling logic
- **Fix**: Check UpdateStateMachineJob.cs:121 - destination state type check

### Error: "Entry state not found in nested machine"
- **Cause**: EntryState not in NestedStateMachine.States
- **Fix**: Verify SubStateMachineStateAsset.IsValid() returns true

### Crash: Stack overflow during baking
- **Cause**: Circular reference (SubA contains SubA)
- **Fix**: Add circular reference detection in baking

### Warning: "Couldn't find parameter X"
- **Cause**: Nested machine references parameter that doesn't exist at root
- **Fix**: Parameters are shared at root level - ensure all parameters exist

---

## Success Criteria

**Minimum Viable Feature**:
- ✅ Bridge converts Unity sub-state machines
- ✅ Baking creates recursive blob structure
- ✅ Runtime enters sub-state machines correctly
- ✅ No crashes or data corruption

**Full Feature Complete**:
- ✅ Above + Exit transitions work
- ✅ Visual editor shows hierarchy
- ✅ Inspector shows current state path
- ✅ Performance benchmarks meet targets

---

## Next Steps After Testing

1. **If tests pass**: Document for users, consider feature complete
2. **If issues found**: File bugs with specific repro steps
3. **If edge cases fail**: Add validation/error handling
4. **If performance poor**: Profile and optimize

