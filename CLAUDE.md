# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a NinjaTrader 8 indicator project called **TTW VolumeFlow** - a high-performance volume analysis indicator that detects significant volume spikes and marks potential stop/reversal zones. The project contains both original and optimized versions, with the optimized version achieving 97.7% faster execution and 76.5% memory reduction.

## Architecture

### Core Files
- `TTWVolumeFlow_Optimized.cs` - Main optimized indicator (current version M2.5)
- `TTWVolumeFlow.cs` - Original indicator for reference
- `readme.md` - Project documentation and installation guide
- `Project_Info/` - Contains project brief and requirements

### Key Components
The indicator is built using NinjaTrader 8's indicator framework with these core elements:
- **Volume Spike Detection**: Uses SMA-based volume analysis with configurable thresholds
- **ATR-Based Confirmation**: Average True Range for price movement validation
- **ATR Trailing Stop Filter**: Harry's step-ladder approach for trend following
- **Visual Markers**: Customizable signal markers with smart positioning
- **Alert System**: Configurable audio/visual alerts

### Development Commands

Since this is a NinjaTrader 8 indicator project, development follows NinjaScript conventions:

**Compilation**: Use NinjaTrader 8's built-in compiler (F5 in NinjaScript Editor)
**Testing**: Apply indicator to charts within NinjaTrader 8 platform
**Installation**: Copy .cs files to NinjaTrader's Custom/Indicators folder

**No traditional build system** (no .sln, .csproj, npm, or MSBuild files) - NinjaTrader handles compilation internally.

## Development Guidelines

### Performance Considerations
- Avoid per-tick allocations in OnBarUpdate()
- Cache indicator references in OnStateChange()
- Use Series<T> for historical data storage
- Minimize redundant calculations and boxing operations

### NinjaScript Patterns
- Initialize indicators in SetDefaults() and OnStateChange()
- Handle historical vs real-time data differences
- Use proper state management for Calculate modes
- Implement defensive coding for Tick Replay scenarios

### Code Structure
- Properties grouped logically (Core Logic, ATR Filter, UI, Alerts)
- XML documentation for all public parameters
- German comments in original, English in optimized version
- Namespace: `NinjaTrader.NinjaScript.Indicators.TTW`

## Project Status

**Current Version**: M2.5 (ATR Trailing Stop Fix)
**Performance**: 97.7% faster execution, 76.5% memory reduction
**Output Parity**: 100% verified with original implementation

### Recent Changes (M2.5)
- Fixed erratic ATR trailing stop behavior
- Integrated Harry's step-ladder ATR logic
- Stops now only move favorably (long stops up, short stops down)
- Maximum one reversal per bar
- Simplified to single "Show Stop Line" parameter

## Testing

Performance testing uses built-in benchmarking with these metrics:
- Execution time per bar (microseconds)
- Memory usage (MB)
- Total bars processed
- Peak memory consumption

Test with identical datasets and settings for before/after comparison.