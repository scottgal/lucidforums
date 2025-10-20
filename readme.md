# Forum/Chat Hybrid Platform

A modern forum/chat hybrid built with **ASP.NET Core** and **PostgreSQL**, designed for:
- ⚡ Super‑efficient threaded discussions (leveraging Postgres `ltree` for hierarchy).
- 💬 - hybrid social network / chat / forum experience. Allowing the interactivity of social networks with the depth of forums.It adapts to how the user wants to engage.
- 🧩 Community charters that define purpose, values, and exclusions.
- 🤖 AI‑assisted moderation and identity scaffolding.
- 🎨 Lightweight, reactive UI with **HTMX + Alpine.js + Tailwind + DaisyUI**.
- 💡 Flexible sustainability models (ads, subscriptions, expert AMAs, knowledge‑for‑credit loops).

---

## 🚀 Project Vision
This project explores how communities can govern themselves through **charters**, sustain themselves through **ads or subscriptions**, and grow their knowledge commons through **expert Q&A with contribution rebates**.  
It’s not just a forum — it’s a toolkit for **self‑sustaining, identity‑driven communities**.

---

## 🛠️ Tech Stack
- **Backend**: ASP.NET Core (C#)
- **Database**: PostgreSQL (with `ltree` for threaded discussions)
- **Frontend**: HTMX + Alpine.js
- **Styling**: Tailwind CSS + DaisyUI
- **AI Integration**: Optional LLM moderation layer (e.g., via Ollama)

---

## 📂 Features (MVP)
- Threaded discussions with efficient Postgres queries.
- Community charters (purpose, membership, values, exclusions).
- Moderation pipeline powered by LLMs (charter‑aware).
- Configurable monetization primitives:
    - Ads (community‑controlled)
    - Subscriptions
    - Per‑transaction expert sessions
    - Knowledge‑for‑credit rebates

---

## 🧭 Roadmap
- [ ] Scaffold ASP.NET Core + HTMX + Alpine.js frontend
- [ ] Define Postgres schema (threads, posts, charters, users)
- [ ] Implement `ltree`‑based threading
- [ ] Add moderation service (LLM‑driven)
- [ ] Charter generator (LLM‑assisted)
- [ ] Monetization primitives (ads, subs, credits)
- [ ] Deploy first community instance

---

## 📜 Charter Defaults
Every community starts with a baseline charter:
- No commercial content
- No advertising in posts (unless permitted by community charter)
- Content by age group for communit charter (e.g., no adult content in general communities)
- Respectful discussion (constructive critique only; again unless charter states otherwise)
- No illegal content (nudity, hate speech, violence, etc. Non‑negotiable.)

Communities can extend these rules but not weaken them.

---

## 🤝 Contributing
Contributions are welcome!
- Fork the repo and open a PR.
- Suggest improvements to threading, governance, or monetisation models.
- Share ideas for new charter templates.
- Initial goal will be to dogfood the system by creating a few pilot communities.

---

## 📄 License
UnLicense (see LICENSE file)

---

## 🌐 Inspiration
This project is inspired by:
- The resilience of open‑source forums
- The flexibility of graph‑like data models in PostgreSQL
- The potential of AI to scaffold governance without centralizing control