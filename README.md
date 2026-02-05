# 🎮 Phase0 Prototype - Advanced Grid Puzzle Game

<div align="center">

![Unity](https://img.shields.io/badge/Unity-2022.3_LTS-black?style=for-the-badge&logo=unity)
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)

*A sophisticated puzzle prototype featuring clean architecture, smooth animations, and innovative placement mechanics*

[🎯 Key Features](#-key-features) • [🏗️ Architecture](#️-architecture) • [🚀 Quick Start](#-quick-start) • [📋 Requirements](#-requirements)

</div>

---

## ✨ Overview

Phase0 is a cutting-edge puzzle game prototype that demonstrates advanced Unity development techniques. Experience smooth 60fps gameplay with pixel-perfect grid mechanics, stunning Spine 2D animations, and buttery-smooth PrimeTween transitions.

**🎨 Visual Excellence**: Spine-powered cat characters with dynamic hatch overlays and ghost placements
**⚡ Performance**: Zero GC allocation during gameplay loops, optimized for mobile performance
**🎯 Precision**: Sub-pixel accuracy with intelligent snap/rotation systems
**🧪 Quality**: Comprehensive test coverage with Unity Test Framework

---

## 🎯 Key Features

### 🎪 Core Gameplay
- **Smart Grid System**: Global screen grid with flexible rug subset mapping
- **Precise Rotations**: Anchor-based piece rotation around pivot points
- **Collision Detection**: Mathematical validation with blocked/dummy cell support
- **Ghost Previews**: Dynamic placement visualization with outline styling

### 🎨 Visual Effects
- **Spine 2D Animation**: Professional animated characters with state machines
- **PrimeTween Integration**: High-performance tweening for all visual feedback
- **Haptic Feedback**: Mobile vibration support for tactile gaming experience
- **Invalid State Indicators**: World-space diagonal hatch patterns over invalid placements

### 🎛️ Advanced Controls
- **Smooth Dragging**: Hysteresis-based cell switching with debounce mechanics
- **Snap Effects**: Configurable overshoot ratios and bounce-back animations
- **Rotation System**: Grid-aligned rotations with negative coordinate support

---

## 🏗️ Architecture

<div align="center">

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   View Layer    │    │   Core Logic    │    │ Configuration   │
│                 │    │                 │    │                 │
│ • MonoBehaviours│◄──►│ • Pure C# Math │◄──►│ • ScriptableObj │
│ • Spine         │    │ • Placement     │    │ • Scene Config  │
│ • PrimeTween    │    │ • Validation    │    │ • ShapeDefs     │
│ • Haptics       │    │ • Rotations     │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

</div>

### 🧠 Core Separation
Our architecture follows strict separation principles:

**Core Layer** (Pure Logic)
- `Phase0CoreModel.cs` - Mathematical foundations for rotations and collisions
- `Phase0PlacementBrain.cs` - Validation and placement algorithms
- `IntRect.cs` - Grid coordinate utilities

**View Layer** (Unity Specific)
- `Phase0GameController.cs` - Input handling and game flow
- `Phase0GhostTilesView.cs` - Ghost preview rendering
- `Phase0GameFeelFX.cs` - Animation and particle effects

---

## 🚀 Quick Start

### Prerequisites
- **Unity 2022.3 LTS** (exact version required)
- **Required Packages**: PrimeTween, Unity Test Framework, UI, Animation modules

### Setup
1. **Clone & Open**
   ```bash
   git clone <repository-url>
   cd Phase0-Prototype
   ```

2. **Open in Unity**
   - Launch Unity Hub
   - Add project → Select the cloned folder
   - Open scene: `Assets/Scenes/SampleScene.unity`

3. **Resolve Packages**
   - Window → Package Manager
   - Ensure PrimeTween and Test Framework are installed

4. **Run Tests**
   - Unity Test Runner → EditMode → Run All Tests
   - All tests should pass ✅

### 🎮 Gameplay
- **Drag pieces** across the global grid
- **Rotate** with grid-aligned precision (90° increments)
- **Ghost preview** shows placement options
- **Invalid placements** highlighted with hatch patterns

---

## 🔧 Technical Details

### Grid Mechanics
- **Global Grid**: Integer coordinate system covering entire screen
- **Rug Subset**: Highlighted 4x4 central area for visual focus
- **Anchor Rotation**: Pieces rotate around internal pivot points
- **Negative Coordinates**: Full support for negative grid positions

### Performance Optimizations
- **Zero GC Alloc**: Custom data structures avoid managed allocations
- **Frame Budget**: 16.67ms target (60fps) with headroom
- **Shader Optimization**: Mobile-compatible materials with fallbacks

### Animation System
- **PrimeTween**: GPU-accelerated tweening for all effects
- **Snap Transitions**: Configurable overshoot and bounce parameters
- **State Machines**: Spine animation controllers for character behavior

---

## 🧪 Testing & Quality

### Test Coverage
```csharp
✅ Core Logic Tests
✅ Placement Validation
✅ Rotation Mechanics
✅ Grid Coordinate Math
✅ Boundary Conditions
```

### Performance Benchmarks
- **Target FPS**: 60 (stable)
- **GC Pressure**: 0 alloc/frame during drag operations
- **Memory**: Mobile-optimized asset loading

---

## 📋 Configuration

### Game Feel Settings
Located: `Assets/Phase0/Configs/Phase0GameFeelSettings.asset`

```csharp
// Animation tuning
snapDuration: 0.3f
bounceBackDuration: 0.4f
overshootRatio: 0.1f

// Visual effects
hatchAngleDeg: 135°
hatchOpacity: 0.8f
hatchWidth: 0.18f
```

### Scene Configuration
Located: `Assets/Resources/SceneConfig.asset`

```csharp
// Grid dimensions
globalGridWidth: 20
globalGridHeight: 15

// Rug subset (highlighted area)
rugOrigin: (6, 5)
rugWidth: 4
rugHeight: 4
```

---

## 🎨 Art & Animation

### Spine Integration
- **Character**: Cat with cutting animations
- **States**: Idle, Impact, Dragging transitions
- **Optimization**: SkeletonAnimation for world-space rendering

### Visual Effects
- **Ghost Outlines**: Soft blue valid placements, red invalid states
- **Hatch Patterns**: World-space diagonal stripes for invalid feedback
- **Shadows**: Elliptical drop shadows with alpha blending

---

## 📊 Project Structure

```
Assets/
├── Scripts/                 # Core C# logic
│   ├── Phase0*.cs          # Game systems
│   └── *.asmdef            # Assembly definitions
├── Phase0/                  # Project-specific assets
│   ├── Configs/            # ScriptableObjects
│   ├── Shaders/            # Custom Unity shaders
│   └── Generated/          # Runtime-generated textures
├── Spine/                   # Spine 2D animation assets
├── Tests/                   # Unity Test Framework
└── Scenes/                  # Unity scenes
```

---

## 🔍 Advanced Features

### Obstruction System
- **Blocked Cells**: Permanent obstacles (rug-local: (1,1), (2,2))
- **Dummy Pieces**: Simulated occupied spaces
- **Dynamic Updates**: Runtime obstacle placement support

### Input Handling
- **Debounce Logic**: `hoverDebounceSeconds` prevents jitter
- **Hysteresis**: `cellSwitchHysteresisWorld` smooths transitions
- **Mobile Support**: Touch input with haptic feedback

---

## 🚀 Contributing

### Development Guidelines
- **Architecture**: Strict Core/View separation maintained
- **Performance**: Zero GC alloc principle for game loops
- **Testing**: New features require comprehensive test coverage

### Code Style
- **Naming**: PascalCase for classes, camelCase for methods
- **Architecture**: Pure functions in Core, MonoBehaviours in View
- **Documentation**: XML comments for public APIs

---

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

---

<div align="center">

**Built with ❤️ using Unity, Spine, and PrimeTween**

*Experience the future of puzzle gaming prototypes*

</div>
