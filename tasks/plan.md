# Implementation Plan: WinoxIT Hub Consolidation, Logo, AI & Dashboard Redesign

## Overview
Consolidate WinoxIT's 15 fragmented menu panels into 5 cohesive Category Hubs, replace the hamburger toggle with the official project brand logo icon, expand the post-format application catalog with all essential technician software, enlarge the AI query box to 2 lines with multi-query web search, and redesign `web/dashboard.html` with a modern Fluent Dark design.

## Architecture Decisions
1. **5 Cohesive Hubs:**
   - Hub 1: Dashboard & LAN Fleet Hub (`PanelHubDashboard`)
   - Hub 2: Hardware, Drivers & Printer Hub (`PanelHubHardware`)
   - Hub 3: Post-Format Software Deploy & Debloat Hub (`PanelHubPostFormat`)
   - Hub 4: Core System Repair & Maintenance Hub (`PanelHubRepair`)
   - Hub 5: AI Diagnostics & Live Web Search Hub (`PanelHubAI`)
2. **Brand Logo Navigation:**
   - Remove hamburger button (`BtnToggleSidebar`).
   - Use `branding/dist/v3-concept-1-symbol-app-icon-64.png` embedded in the sidebar header as the collapse/expand trigger.
3. **Post-Format Catalog Expansion:**
   - 25+ essential packages across Browsers, Remote IT, Compression, Office/PDF, Media, Developer & Runtimes.
4. **2-Line AI Diagnostic Box:**
   - `MinHeight="64"`, `AcceptsReturn="True"`, `TextWrapping="Wrap"`.
   - Dual-query live web scraping fallback with DuckDuckGo.
5. **Modern Dashboard:**
   - Pure, dependency-free HTML5/CSS3/Vanilla JS with glassmorphism and real-time LAN telemetry.

## Task List Index
- Tasks tracked in `tasks/todo.md`.
