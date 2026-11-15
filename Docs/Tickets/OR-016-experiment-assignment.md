# OR-016: Assign experiment arms from reloadable configuration

**Depends on:** [OR-008](OR-008-authentication.md), [OR-013](OR-013-routing-policies-control.md), [OR-015](OR-015-routing-orchestration.md)  
**References:** [ADR-0005](../ADRs/ADR-0005-experiment-assignment.md); [Requirements](../BusinessRequirements/Order-Router-Technical-Requirements.md) §8.2, AB-01, AB-02, AB-03, SC-25, SC-26

## Description

Add the `IExperimentAssigner` port and `ConfigurationExperimentAssigner`, which reads `experiments.json` through `IOptionsMonitor` (reload on change). Each experiment is targeted first (callers, ZIP prefixes, ZIP ranges), then split by caller using salted SHA-256 buckets (never `string.GetHashCode`). The caller ID is the JWT `sub`, then the client ID; the dev key maps to `dev-key`. A bad config change keeps the last good one. Replace the all-control assignment from OR-015 with this assigner.

## Acceptance criteria

### AC1: Default is control

- **Given** the shipped config with every experiment disabled
- **When** any order is routed
- **Then** every experiment is control with reason `disabled`

### AC2: Not targeted

- **Given** an enabled experiment targeting ZIP prefix `021`
- **When** an order for ZIP `10015` arrives
- **Then** that experiment is control with reason `not_targeted`

### AC3: Same caller, same arm

- **Given** an enabled experiment at 50%
- **When** one caller sends 20 targeted orders
- **Then** all 20 get the same arm

### AC4: Stable across restarts

- **Given** a known caller ID, experiment ID and salt
- **When** the bucket is computed in two processes
- **Then** both give the same bucket

### AC5: Ramp up only adds

- **Given** a caller in treatment at 20%
- **When** the percentage is raised to 50%
- **Then** the caller is still in treatment

### AC6: Reload without restart

- **Given** the API is running
- **When** I edit `experiments.json` to enable an experiment
- **Then** the next request uses the new setting and `config_version` changes

### AC7: Bad edit keeps last good

- **Given** the API is running with valid config
- **When** I save an invalid `experiments.json` (e.g., percentage 150)
- **Then** requests keep the previous arms and an error is logged

### AC8: Arms are independent

- **Given** two enabled experiments at 50%
- **When** many callers are assigned
- **Then** a caller's arm in one experiment does not predict its arm in the other

### AC9: Caller targeting

- **Given** an enabled experiment at 100% targeting the caller `portal-client`
- **When** `portal-client` and `other-client` each send an order
- **Then** `portal-client` is treatment (reason `bucketed`) and `other-client` is control (reason `not_targeted`)

### AC10: ZIP range targeting

- **Given** an enabled experiment at 100% targeting the range `30000-30999`
- **When** orders for ZIP `30500` and `31000` arrive
- **Then** the first is treatment and the second is control with reason `not_targeted`

### AC11: No caller ID

- **Given** an authenticated principal with neither a `sub` nor a client ID claim
- **When** orders with the same `order_id` are assigned
- **Then** the bucket is computed from `order_id` and is the same each time (AB-01)

### AC12: ZIPs in config must be 5 digits

- **Given** the API is running with valid config
- **When** I save `experiments.json` with the ZIP range `2100-2199`
- **Then** the change is rejected, an error is logged and the previous arms stay active
