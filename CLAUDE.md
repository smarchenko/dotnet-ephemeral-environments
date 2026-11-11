# Branch-based deployment PoC

Sessions may restart abruptly. **On start, read `docs/STATUS.md`** — it holds goal, decisions,
current stage and next step. **Update `docs/STATUS.md` after every decision or completed step.**

- Communicate with the user in Ukrainian.
- **Never commit.** The user reviews and commits. Leave changes in the working tree.
- Build the PoC in **small stages**; stop after each step for the user to try it.
- **After each step, report:** what was added, how it works, and a step-by-step guide to run and test it.
- **Keep `README.md` up to date** with run instructions (every step that changes how to run/test updates it).
- Stack: .NET 10 (MVC Web + API), PostgreSQL, Docker (Linux engine: `DOCKER_CONTEXT=desktop-linux`);
  later CI (GitHub Actions / Azure DevOps) + Hetzner (Terraform) / Azure (Bicep), optional k8s.
- Specs: `docs/superpowers/specs/`, plans: `docs/superpowers/plans/`.
