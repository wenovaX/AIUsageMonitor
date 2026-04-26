# 📡 AIUsageMonitor

> **Premium AI Usage Monitoring Dashboard**  
> Effortlessly track your quotas and limits across Antigravity and OpenAI Codex in one sleek, unified interface.

![AIUsageMonitor Header](https://raw.githubusercontent.com/username/repo/main/Resources/Images/app_title.png)

## ✨ Overview

AIUsageMonitor is a .NET MAUI-based desktop application designed for AI power users who manage multiple accounts and models. It provides real-time visibility into usage percentages, reset times, and credit balances, ensuring you never hit an unexpected limit during critical work.

## 🚀 Key Features

### 🏢 Multi-Service Support
- **Antigravity (Google)**: Track Gemini 3 Flash, 3.1 Pro (High/Low), Claude Opus/Sonnet 4.6, and GPT-OSS 120B.
- **Codex (OpenAI/GitHub)**: Monitor session-based limits and weekly quotas with intelligent window normalization.

### 🎨 Premium User Experience
- **Modern Dark UI**: A sleek, Slate-based dark theme with vibrant emerald and sky-blue accents.
- **Anonymous Mode**: Instantly mask your personal information (name, email) for privacy or screen sharing.
- **Tabbed Interface**: Seamlessly switch between Google models, Codex accounts, and settings.

### ⚙️ Full Customization
- **Model Filters**: Define your own tracking keywords and display names.
- **Flexible Ordering**: Reorder model cards to prioritize what matters most to you.
- **Auto-Refresh & Retry**: Built-in reliability with configurable retry logic and manual refresh options.

## 🛠️ Setup & Installation

### Prerequisites
- .NET 8.0 or 10.0 SDK
- Windows 10/11 (for the desktop app)

### Building from Source
1. Clone the repository.
2. Open `AIUsageMonitor.sln` in Visual Studio 2022.
3. Restore NuGet packages.
4. Build and Run (Target: Windows Machine).

## 🔑 Authentication Guide

### Antigravity (Google)
1. Ensure the Antigravity extension is installed and signed in within your IDE.
2. Click the **(+)** button in the Antigravity tab.
3. Complete the Google OAuth flow in your browser.

### Codex (OpenAI / GitHub)
1. Switch to the **Codex** tab and click **(+)**.
2. **OpenAI Login**: Uses PKCE OAuth flow for direct OpenAI account access.
3. **GitHub Login**: Uses Device Flow for users accessing models via GitHub Copilot.

## 🛡️ Privacy & Security
- **Local Storage**: All account tokens and configurations are stored locally on your machine.
- **No Middleman**: The app communicates directly with Google and OpenAI APIs.
- **Open Source**: Review the code to ensure your tokens are handled securely.

## 📄 License
Distributed under the MIT License. See `LICENSE` for more information.
