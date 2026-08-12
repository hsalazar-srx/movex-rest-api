# MOVEX REST API: Claude Code Instructions

**Project:** .NET 8 REST API for M3 MI transactions — config-driven, hot-reload
**Stack:** ASP.NET Core 8.0, M3 MI API, DB2/AS400

---

## Multi-Agent System (MAS)

**Primary Agents:**
- **@expert-movex-dotnet** — M3 transactions, MOVEX database, MI program knowledge
- **@developer-dotnet** — .NET API implementation, service layer
- **@architect-system-design** — ADRs, design reviews

**Skill definitions:** `C:\Projects\.github\skills\manifest.json`

---

## Adaptive Learning

**Before starting significant work**, check for prior lessons:
1. Read `ai/memory/learnings.md` for accumulated project learnings
2. Run `/cognee-query` with your specific question for deeper context
3. For M3 work: run `/m3-lookup` to check known quirks for the program/table involved
4. For bug fixes: check if the broken behavior was an assumption in any ADR

**After completing significant work**, capture what you learned:
- Run `/capture-learning` to record pitfalls, patterns, quirks, or mistakes
- M3 quirks are especially valuable — they prevent rediscovery across projects

---

## Session Checklist

**Start:** Read ai/memory/learnings.md → /plan for complex tasks
**During:** Check skills manifest → /m3-lookup for MI programs → Write tests
**End:** Tests pass → No credentials/PII → /capture-learning if applicable → **Run generate-openapi** → Commit

## Before Every Commit / PR

If you added, removed, or changed any controller routes, request/response models, or transaction JSON configs, regenerate the OpenAPI spec or the CI pipeline will fail:

```powershell
cd C:\Projects\MOVEX\API-Integration\movex-rest-api
.\scripts\generate-openapi.ps1
```

Then stage the updated spec file alongside your other changes.

---

**Version:** 1.0
**Last Updated:** 2026-03-26
