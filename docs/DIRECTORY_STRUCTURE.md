# Documentation Directory Structure

This document provides an overview of the documentation organization.

---

## 📂 Directory Tree

```
docs/
├── README.md                           # ← You are here (Main index)
├── CHANGELOG.md                        # Version history
├── CONTRIBUTING.md                     # Contribution guidelines
│
├── scdb/                               # SCDB Single-File Format Documentation
│   ├── README_INDEX.md                 # SCDB documentation index
│   ├── README.md                       # Quick start & overview
│   ├── FILE_FORMAT_DESIGN.md          # Complete technical spec (70 pages) ⭐
│   ├── DESIGN_SUMMARY.md              # Executive summary
│   ├── IMPLEMENTATION_STATUS.md       # Progress tracking
│   └── PHASE1_IMPLEMENTATION.md       # Phase 1 technical details
│
├── migration/                          # Migration Documentation
│   ├── README.md                       # Migration guide index
│   └── MIGRATION_GUIDE.md             # Complete migration guide ⭐
│
└── development/                        # Development Documentation
    ├── README.md                       # Development docs index
    ├── SCDB_COMPILATION_FIXES.md      # Compilation fixes (English)
    └── SCDB_COMPILATION_FIXES_NL.md   # Compilation fixes (Dutch)
```

---

## 📚 Quick Navigation

### By Role

#### **End Users**
Start here: [Main README](../README.md) → [SCDB Overview](./scdb/README.md)

#### **Database Administrators**
Migration: [Migration Guide](./migration/MIGRATION_GUIDE.md)

#### **Developers/Contributors**
Development: [Development README](./development/README.md) → [SCDB Status](./scdb/IMPLEMENTATION_STATUS.md)

#### **Architects/Decision Makers**
Design: [Design Summary](./scdb/DESIGN_SUMMARY.md)

### By Topic

#### **SCDB Format**
- Overview: [scdb/README.md](./scdb/README.md)
- Full Spec: [scdb/FILE_FORMAT_DESIGN.md](./scdb/FILE_FORMAT_DESIGN.md)
- Status: [scdb/IMPLEMENTATION_STATUS.md](./scdb/IMPLEMENTATION_STATUS.md)

#### **Migration**
- Guide: [migration/MIGRATION_GUIDE.md](./migration/MIGRATION_GUIDE.md)
- API: See guide Section 2

#### **Development**
- Compilation Fixes: [development/SCDB_COMPILATION_FIXES.md](./development/SCDB_COMPILATION_FIXES.md)
- Contributing: [CONTRIBUTING.md](./CONTRIBUTING.md)

---

## 📊 File Sizes (Approximate)

| File | Pages | LOC | Purpose |
|------|-------|-----|---------|
| FILE_FORMAT_DESIGN.md | ~70 | ~6500 | Complete spec |
| MIGRATION_GUIDE.md | ~35 | ~800 | Migration guide |
| SCDB_COMPILATION_FIXES.md | ~20 | ~400 | Dev fixes |
| IMPLEMENTATION_STATUS.md | ~15 | ~500 | Progress |
| PHASE1_IMPLEMENTATION.md | ~10 | ~350 | Phase 1 details |
| DESIGN_SUMMARY.md | ~8 | ~300 | Executive summary |

---

## 🎯 Documentation Goals

### 1. **Accessibility**
- Clear navigation structure
- Multiple entry points
- Indexed by role and topic

### 2. **Completeness**
- User guides
- Technical specifications
- API documentation
- Development guides

### 3. **Maintainability**
- Organized by topic
- Clear naming conventions
- Cross-references

### 4. **Discoverability**
- README files in each directory
- Main index with quick links
- Search-friendly structure

---

## 🔄 Document Flow

```
User Journey:

New User
  └─→ docs/README.md
      └─→ scdb/README.md
          └─→ scdb/FILE_FORMAT_DESIGN.md (optional)

Migrating User
  └─→ docs/README.md
      └─→ migration/MIGRATION_GUIDE.md

Contributing Developer
  └─→ docs/README.md
      └─→ development/README.md
          └─→ scdb/IMPLEMENTATION_STATUS.md
              └─→ development/SCDB_COMPILATION_FIXES.md

Architect/PM
  └─→ docs/README.md
      └─→ scdb/DESIGN_SUMMARY.md
          └─→ scdb/IMPLEMENTATION_STATUS.md
```

---

## 📖 Naming Conventions

### Directory Names
- **lowercase** - All subdirectories use lowercase
- **singular** - Use singular form (e.g., `migration` not `migrations`)
- **descriptive** - Clear purpose (e.g., `development` not `dev`)

### File Names
- **UPPERCASE.md** - Major documentation (e.g., `README.md`, `MIGRATION_GUIDE.md`)
- **PascalCase.md** - Technical specs (e.g., `FileFormatDesign.md`)
- **SCREAMING_SNAKE_CASE.md** - Status/meta docs (e.g., `IMPLEMENTATION_STATUS.md`)

### Prefixes
- **SCDB_*** - SCDB-specific documentation
- **README** - Directory index
- No prefix - General project documentation

---

## 🌍 Translations

### Available Languages
- 🇬🇧 **English** - Primary language (all docs)
- 🇳🇱 **Dutch** - Selected docs (suffix: `_NL`)

### Translation Guidelines
1. Keep structure identical to English version
2. Translate content, preserve code examples
3. Add suffix to filename (e.g., `GUIDE_NL.md`)
4. Link from main document

### Requesting Translations
Open an issue with `translation` label.

---

## 🔗 Cross-References

### Internal Links
Use relative paths:
```markdown
[Migration Guide](./migration/MIGRATION_GUIDE.md)
[SCDB Overview](./scdb/README.md)
```

### External Links
Use absolute URLs:
```markdown
[PostgreSQL FSM](https://www.postgresql.org/docs/current/storage-fsm.html)
```

---

## 📝 Maintenance

### Adding New Documentation

1. **Create file** in appropriate subdirectory
2. **Update README.md** in that directory
3. **Update main docs/README.md**
4. **Update DIRECTORY_STRUCTURE.md** (this file)
5. **Add cross-references** in related docs

### Updating Existing Documentation

1. **Update file** content
2. **Check links** still valid
3. **Update "Last Updated"** date
4. **Update version** if major change

### Removing Documentation

1. **Archive** instead of deleting (if historical value)
2. **Update all links** to archived location
3. **Update indexes**

---

## 🚀 Future Plans

### Planned Additions
- [ ] API Reference (auto-generated from XML comments)
- [ ] Tutorial Series (step-by-step guides)
- [ ] Video Tutorials (links to external)
- [ ] FAQ Section
- [ ] Troubleshooting Guide

### Planned Improvements
- [ ] Search functionality
- [ ] Interactive examples
- [ ] Diagram/visualization tools
- [ ] Versioned documentation

---

## 📄 License

All documentation licensed under MIT. See [LICENSE](../LICENSE).

---

**Last Updated:** 2026-01-XX  
**Maintained by:** SharpCoreDB Contributors  
**Questions?** Open an issue on GitHub
