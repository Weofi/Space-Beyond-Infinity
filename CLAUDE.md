# Voidward — Claude entry point

@AGENTS.md

Follow the imported project instructions in `AGENTS.md`.
It contains the project context, issue workflow, testing playbook and mandatory living-wiki maintenance rules.

The browsable developer wiki starts at [Docs/Wiki/README.md](Docs/Wiki/README.md).
For the visual browser view, open `Docs/Wiki/index.html`. Keep screenshot selection and
the reader's introductions in `Tools/Docs/wiki_content.json` aligned with the technical source.
The default view is the German player handbook. Maintain its task-based instructions in
`Tools/Docs/player_guide.json` when gameplay, controls, prerequisites or menu labels change.
Check the actual implementation; keep planned features separate from playable actions.
After relevant changes, update `AGENTS.md` and regenerate it with
`python3 Tools/Docs/build_wiki.py`; verify with `python3 Tools/Docs/build_wiki.py --check`.
Keep implementation and documentation in the same change, and report what was verified.
