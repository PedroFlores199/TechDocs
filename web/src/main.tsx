import React, { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  BookOpen,
  LayoutDashboard,
  Files,
  Boxes,
  Upload,
  ClipboardCheck,
  History,
  Search,
  ArrowUpRight,
  ArrowRight,
  Plus,
  Download,
  X,
  Check,
  ChevronRight,
  Clock,
  FileText,
  ShieldCheck,
  AlertTriangle,
  Settings2,
  LogOut,
  Trash2,
  Menu,
} from "lucide-react";
import { diffLines } from "diff";
import "./style.css";

type Item = Record<string, any>;
type Block = { type: string; text: string; partCode?: string };
const states: Record<string, string> = {
  Draft: "Borrador",
  InReview: "En revisión",
  Approved: "Aprobado",
  Obsolete: "Obsoleto",
};
const tabs = [
  { id: "dashboard", label: "Resumen", icon: LayoutDashboard },
  { id: "documents", label: "Documentación", icon: Files },
  { id: "parts", label: "Catálogo de piezas", icon: Boxes },
  { id: "imports", label: "Importaciones", icon: Upload },
  { id: "reviews", label: "Bandeja de revisión", icon: ClipboardCheck },
  { id: "audit", label: "Registro de actividad", icon: History },
];
const date = (s: string) =>
  new Date(s).toLocaleString("es-ES", {
    day: "2-digit",
    month: "short",
    hour: "2-digit",
    minute: "2-digit",
  });
function App() {
  const [session, setSession] = useState<Item | null>(() => {
    try {
      return JSON.parse(sessionStorage.getItem("techdocs") || "null");
    } catch {
      return null;
    }
  });
  const [tab, setTab] = useState("documents");
  const [dashboard, setDashboard] = useState<Item>({});
  const [systems, setSystems] = useState<Item[]>([]);
  const [docs, setDocs] = useState<Item>({ items: [], total: 0 });
  const [parts, setParts] = useState<Item>({ items: [], total: 0 });
  const [imports, setImports] = useState<Item[]>([]);
  const [tasks, setTasks] = useState<Item[]>([]);
  const [audit, setAudit] = useState<Item>({ items: [], total: 0 });
  const [query, setQuery] = useState("");
  const [state, setState] = useState("");
  const [system, setSystem] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const [order, setOrder] = useState("recent");
  const [page, setPage] = useState(1);
  const [refresh, setRefresh] = useState(0);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [detail, setDetail] = useState<Item | null>(null);
  const [partDetail, setPartDetail] = useState<Item | null>(null);
  const [editor, setEditor] = useState<Item | null>(null);
  const [diff, setDiff] = useState<Item | null>(null);
  const [diffFrom, setDiffFrom] = useState(1);
  const [semanticEnabled, setSemanticEnabled] = useState(false);
  const [semanticOpen, setSemanticOpen] = useState(false);
  const [question, setQuestion] = useState("");
  const [semanticResults, setSemanticResults] = useState<Item[]>([]);
  const [mobileNav, setMobileNav] = useState(false);
  const [reason, setReason] = useState("");
  const role = session?.user.role;
  async function api(path: string, options: RequestInit = {}) {
    const r = await fetch("/api" + path, {
      ...options,
      headers: {
        ...(options.body instanceof FormData
          ? {}
          : { "Content-Type": "application/json" }),
        Authorization: "Bearer " + session?.token,
        ...options.headers,
      },
    });
    if (!r.ok) {
      const data = await r.json().catch(() => ({}));
      if (r.status === 401) {
        sessionStorage.removeItem("techdocs");
        setSession(null);
      }
      throw new Error(
        data.title ||
          (r.status === 403
            ? "Tu rol no permite esta acción."
            : `No se pudo completar la operación (${r.status}).`),
      );
    }
    if (r.status === 204) return null;
    return r.headers.get("content-type")?.includes("application/json")
      ? r.json()
      : r.blob();
  }
  async function action(fn: () => Promise<void>) {
    setBusy(true);
    setError("");
    try {
      await fn();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function login(r: string) {
    await action(async () => {
      const response = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          email: r.toLowerCase() + "@techdocs.demo",
          password: "Demo1234!",
        }),
      });
      if (!response.ok)
        throw new Error("No se pudo iniciar la sesión de demostración.");
      const s = await response.json();
      sessionStorage.setItem("techdocs", JSON.stringify(s));
      setSession(s);
    });
  }
  useEffect(() => {
    if (!session) return;
    let active = true;
    Promise.all([
      api("/dashboard"),
      api("/systems"),
      api("/imports"),
      api("/tasks"),
      api("/semantic/status"),
    ])
      .then(([d, s, i, t, semantic]) => {
        if (active) {
          setDashboard(d);
          setSystems(s);
          setImports(i);
          setTasks(t);
          setSemanticEnabled(semantic.enabled);
        }
      })
      .catch((e) => active && setError(e.message));
    return () => {
      active = false;
    };
  }, [session, refresh]);
  useEffect(() => {
    if (!session) return;
    let active = true;
    const timer = setTimeout(() => {
      const qs = new URLSearchParams({
        q: query,
        page: String(page),
        ...(state || tab === "reviews"
          ? { state: tab === "reviews" ? "InReview" : state }
          : {}),
        ...(system ? { systemId: system } : {}),
      });
      Promise.all([
        api("/documents?" + qs),
        api("/parts?" + new URLSearchParams({ q: query, page: String(page) })),
        api("/audit?page=" + page),
      ])
        .then(([d, p, a]) => {
          if (active) {
            setDocs(d);
            setParts(p);
            setAudit(a);
          }
        })
        .catch((e) => active && setError(e.message));
    }, 200);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [session, query, state, system, page, tab, refresh]);
  useEffect(() => {
    if (
      tab !== "imports" ||
      !imports.some((x) => ["Queued", "Processing"].includes(x.status))
    )
      return;
    const t = setInterval(() => setRefresh((x) => x + 1), 2000);
    return () => clearInterval(t);
  }, [tab, imports]);
  useEffect(() => {
    if (!notice) return;
    const t = setTimeout(() => setNotice(""), 5000);
    return () => clearTimeout(t);
  }, [notice]);
  function navigate(t: string) {
    setTab(t);
    setPage(1);
    setQuery("");
    setState("");
    setMobileNav(false);
    setRefresh((x) => x + 1);
  }
  async function openDocument(id: string, version?: number) {
    await action(async () => {
      setDetail(
        await api(`/documents/${id}${version ? "?version=" + version : ""}`),
      );
      setDiff(null);
    });
  }
  async function transition(a: string) {
    if (!detail) return;
    await action(async () => {
      await api(`/documents/${detail.id}/transition`, {
        method: "POST",
        body: JSON.stringify({
          action: a,
          expectedRevision: detail.revision,
          reason,
        }),
      });
      setNotice("Estado actualizado y registrado en auditoría.");
      setDetail(await api(`/documents/${detail.id}`));
      setRefresh((x) => x + 1);
      setReason("");
    });
  }
  async function save() {
    if (!editor) return;
    await action(async () => {
      const result = await api(
        "/documents" + (editor.id ? "/" + editor.id : ""),
        {
          method: editor.id ? "PUT" : "POST",
          body: JSON.stringify({
            code: editor.code,
            title: editor.title,
            systemId: editor.systemId,
            blocks: editor.blocks,
            partIds: editor.partIds,
            expectedRevision: editor.revision,
          }),
        },
      );
      setEditor(null);
      setDetail(await api("/documents/" + (editor.id || result.id)));
      setRefresh((x) => x + 1);
      setNotice("Versión guardada.");
    });
  }
  async function exportPdf() {
    await action(async () => {
      const blob = await api(
        "/manual.pdf?" +
          new URLSearchParams({
            ...(system ? { systemId: system } : {}),
            ...(selected.length ? { ids: selected.join(",") } : {}),
          }),
      );
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "TechDocs-Manual.pdf";
      a.click();
      URL.revokeObjectURL(url);
      setNotice("Manual de versiones aprobadas exportado.");
    });
  }
  const statusBadge = (s: string) => (
    <span className={"badge " + s}>
      <i />
      {states[s] || s}
    </span>
  );
  const docCards = (items: Item[]) =>
    items.length ? (
      <div className="table-wrap document-table">
        <table>
          <thead>
            <tr>
              <th className="check-column">
                <input
                  type="checkbox"
                  aria-label="Seleccionar página"
                  checked={items.every((d) => selected.includes(d.id))}
                  onChange={(e) =>
                    setSelected(e.target.checked ? items.map((d) => d.id) : [])
                  }
                />
              </th>
              <th>Referencia</th>
              <th>Título</th>
              <th>Sistema</th>
              <th>Estado</th>
              <th>Versión</th>
              <th>Modificado</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {[...items]
              .sort((a, b) =>
                order === "title"
                  ? a.title.localeCompare(b.title)
                  : order === "code"
                    ? a.code.localeCompare(b.code)
                    : 0,
              )
              .map((d) => (
                <tr
                  key={d.id}
                  className={selected.includes(d.id) ? "selected-row" : ""}
                >
                  <td>
                    <input
                      type="checkbox"
                      aria-label={"Seleccionar " + d.code}
                      checked={selected.includes(d.id)}
                      onChange={(e) =>
                        setSelected(
                          e.target.checked
                            ? [...selected, d.id]
                            : selected.filter((id) => id !== d.id),
                        )
                      }
                    />
                  </td>
                  <td className="reference-column">{d.code}</td>
                  <td>
                    <button
                      className="document-title"
                      onClick={() => openDocument(d.id)}
                    >
                      <FileText size={18} />
                      <span>
                        <strong>{d.title}</strong>
                      </span>
                    </button>
                  </td>
                  <td>{d.system}</td>
                  <td>{statusBadge(d.state)}</td>
                  <td className="mono">v{d.version}</td>
                  <td className="nowrap">{date(d.updatedAt)}</td>
                  <td>
                    <button
                      className="open-document"
                      aria-label={"Abrir " + d.code}
                      onClick={() => openDocument(d.id)}
                    >
                      <ChevronRight size={16} />
                    </button>
                  </td>
                </tr>
              ))}
          </tbody>
        </table>
      </div>
    ) : (
      <div className="empty">
        <Search size={24} />
        <h3>No se encontraron documentos</h3>
        <p>Cambia los filtros o crea un documento nuevo.</p>
      </div>
    );
  if (!session)
    return (
      <div className="login">
        <div className="login-brand">
          <BookOpen size={32} />
          <span>TechDocs</span>
        </div>
        <div className="login-card">
          <span className="eyebrow">Documentación técnica · Atlas R-240</span>
          <h1>Acceso a TechDocs</h1>
          <p>Selecciona un perfil para abrir el catálogo de demostración.</p>
          <div className="login-roles">
            {["Redactor", "Revisor", "Aprobador", "Lector"].map((r) => (
              <button disabled={busy} key={r} onClick={() => login(r)}>
                <ShieldCheck size={20} />
                <span>
                  Entrar como <strong>{r}</strong>
                </span>
                <ArrowRight size={18} />
              </button>
            ))}
          </div>
          <small>
            Demostración con datos ficticios. Cada perfil tiene permisos
            diferentes.
          </small>
          {error && (
            <div className="error" role="alert">
              {error}
            </div>
          )}
        </div>
        <footer>Pedro Flores · C# / .NET portfolio</footer>
      </div>
    );
  return (
    <div className="app-shell">
      <aside className={mobileNav ? "sidebar open" : "sidebar"}>
        <a
          className="brand"
          href="#"
          onClick={(e) => {
            e.preventDefault();
            navigate("dashboard");
          }}
        >
          <BookOpen size={27} />
          <span>
            TechDocs<small>Gestor documental</small>
          </span>
        </a>
        <div className="workspace">
          <span className="workspace-icon">A</span>
          <div>
            <strong>Atlas Engineering</strong>
            <small>Unidad ferroviaria R-240</small>
          </div>
          <ChevronRight size={15} />
        </div>
        <div className="nav-label">Navegación</div>
        <nav>
          {tabs.map((t) => (
            <button
              key={t.id}
              className={tab === t.id ? "active" : ""}
              onClick={() => navigate(t.id)}
            >
              <t.icon size={19} />
              <span>{t.label}</span>
              {t.id === "reviews" && dashboard.inReview > 0 && (
                <b>{dashboard.inReview}</b>
              )}
            </button>
          ))}
        </nav>
        <div className="saved-views">
          <div className="nav-label">Vistas guardadas</div>
          {[
            ["Draft", "Borradores", dashboard.drafts],
            ["InReview", "Pendientes de revisión", dashboard.inReview],
            ["Approved", "Aprobados", dashboard.approved],
          ].map(([s, label, n]) => (
            <button
              key={String(s)}
              onClick={() => {
                setTab("documents");
                setState(String(s));
                setQuery("");
                setPage(1);
              }}
            >
              <Files size={17} />
              <span>{label}</span>
              <b>{n}</b>
            </button>
          ))}
        </div>
        <div className="sidebar-bottom">
          <div className="environment">
            <span className="dot" /> Entorno de demostración
            <p>Datos ficticios · cambios persistentes</p>
          </div>
          <div className="profile">
            <span className="avatar">PF</span>
            <div>
              <strong>Pedro Flores</strong>
              <select
                aria-label="Cambiar rol de demostración"
                value={role}
                onChange={(e) => login(e.target.value)}
              >
                {["Redactor", "Revisor", "Aprobador", "Lector"].map((r) => (
                  <option key={r}>{r}</option>
                ))}
              </select>
            </div>
            <button
              title="Cerrar sesión"
              aria-label="Cerrar sesión"
              onClick={() => {
                sessionStorage.removeItem("techdocs");
                setSession(null);
              }}
            >
              <LogOut size={17} />
            </button>
          </div>
        </div>
      </aside>
      <div className="main-shell">
        <header className="topbar">
          <button
            className="mobile-menu"
            aria-label="Abrir menú"
            onClick={() => setMobileNav(!mobileNav)}
          >
            <Menu />
          </button>
          <form
            className="global-search"
            onSubmit={(e) => {
              e.preventDefault();
              setTab("documents");
              setPage(1);
            }}
          >
            <Search size={19} />
            <input
              aria-label="Búsqueda global"
              placeholder="Buscar documentos"
              value={query}
              onChange={(e) => {
                setQuery(e.target.value);
                setTab("documents");
                setPage(1);
              }}
            />
          </form>
          <div className="header-account">
            <ShieldCheck size={18} />
            <select
              aria-label="Perfil activo"
              value={role}
              onChange={(e) => login(e.target.value)}
            >
              {["Redactor", "Revisor", "Aprobador", "Lector"].map((r) => (
                <option key={r}>{r}</option>
              ))}
            </select>
            <button
              aria-label="Cerrar sesión"
              onClick={() => {
                sessionStorage.removeItem("techdocs");
                setSession(null);
              }}
            >
              <LogOut size={17} />
            </button>
          </div>
        </header>
        <main>
          {error && (
            <div className="error" role="alert">
              <AlertTriangle size={18} />
              {error}
              <button aria-label="Cerrar aviso" onClick={() => setError("")}>
                <X size={16} />
              </button>
            </div>
          )}
          {notice && (
            <div className="notice" role="status">
              <Check size={18} />
              {notice}
            </div>
          )}
          <div className="page-heading">
            <div>
              <span className="eyebrow">Atlas R-240 / Control documental</span>
              <h1>
                {
                  (
                    {
                      dashboard: "Resumen del proyecto",
                      documents: "Documentos",
                      parts: "Catálogo de piezas",
                      imports: "Centro de importaciones",
                      reviews: "Bandeja de revisión",
                      audit: "Registro de actividad",
                    } as Item
                  )[tab]
                }
              </h1>
              <p>
                {
                  (
                    {
                      dashboard:
                        "Estado del catálogo Atlas R-240 y últimas modificaciones.",
                      documents:
                        "Procedimientos de mantenimiento, versiones y estados de aprobación.",
                      parts:
                        "Trazabilidad de componentes y análisis de impacto documental.",
                      imports:
                        "Carga de catálogos Excel y XML. Validación y registro de resultados.",
                      reviews:
                        "Documentos pendientes y tareas generadas por cambios en piezas.",
                      audit:
                        "Historial de cambios de documentos, piezas e importaciones.",
                    } as Item
                  )[tab]
                }
              </p>
            </div>
            <div className="heading-actions">
              <button
                className="button secondary"
                disabled={busy}
                onClick={exportPdf}
              >
                <Download size={16} />
                Exportar manual
              </button>
              {role === "Redactor" && (
                <button
                  className="button primary"
                  onClick={() =>
                    setEditor({
                      code: "",
                      title: "",
                      systemId: systems[0]?.id,
                      blocks: [{ type: "step", text: "" }],
                      partIds: [],
                    })
                  }
                >
                  <Plus size={17} />
                  Nuevo documento
                </button>
              )}
            </div>
          </div>
          {tab === "dashboard" && (
            <>
              <div className="stats-grid">
                {[
                  {
                    label: "Documentos técnicos",
                    value: dashboard.documents,
                    icon: Files,
                    sub: "En el espacio de trabajo",
                  },
                  {
                    label: "Documentación aprobada",
                    value: dashboard.approved,
                    icon: ShieldCheck,
                    sub: "Lista para consultar",
                    good: true,
                  },
                  {
                    label: "Pendientes de revisión",
                    value: dashboard.inReview,
                    icon: Clock,
                    sub: "En la bandeja del equipo",
                  },
                  {
                    label: "Piezas del catálogo",
                    value: dashboard.parts,
                    icon: Boxes,
                    sub: `${dashboard.undocumented || 0} sin documentar`,
                  },
                ].map((s) => (
                  <div className="stat" key={s.label}>
                    <div>
                      <span>{s.label}</span>
                      <s.icon size={19} />
                    </div>
                    <strong>{s.value ?? "—"}</strong>
                    <small className={s.good ? "good" : ""}>
                      {s.good && <span className="dot" />}
                      {s.sub}
                    </small>
                  </div>
                ))}
              </div>
              <div className="overview-row">
                <section className="search-feature">
                  <span className="eyebrow">Búsqueda</span>
                  <h2>Buscar en la documentación</h2>
                  <p>Busca por pieza, sistema o procedimiento.</p>
                  <form
                    onSubmit={(e) => {
                      e.preventDefault();
                      setTab("documents");
                      setPage(1);
                    }}
                  >
                    <Search size={20} />
                    <input
                      aria-label="Buscar documentación"
                      placeholder="Prueba: junta tórica o filtro hidráulico"
                      value={query}
                      onChange={(e) => setQuery(e.target.value)}
                    />
                    <button aria-label="Buscar">
                      <ArrowRight size={20} />
                    </button>
                  </form>
                  <div className="search-hints">
                    <span>Explorar</span>
                    {["Hidráulica", "Frenado", "Tracción"].map((s) => (
                      <button
                        key={s}
                        onClick={() => {
                          setSystem(
                            systems.find((x) => x.name === s)?.id || "",
                          );
                          setTab("documents");
                          setQuery("");
                        }}
                      >
                        {s}
                        <ArrowUpRight size={12} />
                      </button>
                    ))}
                  </div>
                </section>
                <section className="panel approval-panel">
                  <div className="section-top">
                    <h2>Estado documental</h2>
                    <span className="subtle">
                      {dashboard.documents || 0} en total
                    </span>
                  </div>
                  <div className="distribution">
                    {[
                      ["Approved", dashboard.approved],
                      ["InReview", dashboard.inReview],
                      ["Draft", dashboard.drafts],
                    ].map(([s, n]) => (
                      <div key={s}>
                        <span
                          className={"bar " + s}
                          style={{
                            width: `${Math.max(2, (Number(n) / Math.max(1, dashboard.documents)) * 100)}%`,
                          }}
                        />
                      </div>
                    ))}
                  </div>
                  <div className="state-list">
                    {[
                      ["Approved", dashboard.approved],
                      ["InReview", dashboard.inReview],
                      ["Draft", dashboard.drafts],
                    ].map(([s, n]) => (
                      <button
                        key={s}
                        onClick={() => {
                          setState(String(s));
                          setTab("documents");
                        }}
                      >
                        {statusBadge(String(s))}
                        <strong>{n || 0}</strong>
                        <ChevronRight size={15} />
                      </button>
                    ))}
                  </div>
                </section>
              </div>
              <div className="section-top">
                <div>
                  <h2>Documentos recientes</h2>
                  <p>Los últimos procedimientos del equipo</p>
                </div>
                <button
                  className="text-button"
                  onClick={() => navigate("documents")}
                >
                  Ver documentación
                  <ArrowRight size={15} />
                </button>
              </div>
              {docCards(docs.items.slice(0, 3))}
              <div className="lower-grid">
                <section className="panel">
                  <div className="section-top">
                    <h2>Actividad reciente</h2>
                    <History size={18} />
                  </div>
                  {(dashboard.recent || []).slice(0, 4).map((a: Item) => (
                    <div className="activity" key={a.id}>
                      <span className="activity-icon">
                        <FileText size={16} />
                      </span>
                      <div>
                        <strong>
                          {a.action
                            .replace("document.", "Documento · ")
                            .replace("part.", "Pieza · ")}
                        </strong>
                        <p>{a.detail.slice(0, 95)}</p>
                      </div>
                      <time>{date(a.at)}</time>
                    </div>
                  ))}
                </section>
                <section className="panel next-step">
                  <span className="eyebrow">Piezas y procedimientos</span>
                  <h2>Revisión de componentes</h2>
                  <p>
                    Actualiza una ficha y encuentra los documentos que necesitan
                    revisión.
                  </p>
                  <button
                    className="text-button"
                    onClick={() => navigate("parts")}
                  >
                    Explorar el catálogo
                    <ArrowRight size={16} />
                  </button>
                  <div className="mini-flow">
                    <Boxes size={22} />
                    <span />
                    <Files size={22} />
                    <span />
                    <ClipboardCheck size={22} />
                  </div>
                </section>
              </div>
            </>
          )}
          {["documents", "reviews", "parts"].includes(tab) && (
            <div className="toolbar">
              <label className="search-field">
                <Search size={18} />
                <input
                  value={query}
                  aria-label="Buscar en la lista"
                  placeholder={
                    tab === "parts"
                      ? "Buscar pieza o referencia…"
                      : "Buscar por título, referencia o contenido…"
                  }
                  onChange={(e) => {
                    setQuery(e.target.value);
                    setPage(1);
                  }}
                />
              </label>
              {tab !== "parts" && (
                <>
                  <select
                    aria-label="Filtrar sistema"
                    value={system}
                    onChange={(e) => {
                      setSystem(e.target.value);
                      setPage(1);
                    }}
                  >
                    <option value="">Todos los sistemas</option>
                    {systems.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.name}
                      </option>
                    ))}
                  </select>
                  {tab !== "reviews" && (
                    <select
                      aria-label="Filtrar estado"
                      value={state}
                      onChange={(e) => {
                        setState(e.target.value);
                        setPage(1);
                      }}
                    >
                      <option value="">Todos los estados</option>
                      {Object.entries(states).map(([v, l]) => (
                        <option key={v} value={v}>
                          {l}
                        </option>
                      ))}
                    </select>
                  )}
                </>
              )}
              {semanticEnabled && tab !== "parts" && (
                <button
                  className="button secondary"
                  onClick={() => setSemanticOpen(!semanticOpen)}
                >
                  Pregunta semántica
                </button>
              )}
              <button
                className="button secondary reset-filters"
                onClick={() => {
                  setQuery("");
                  setState("");
                  setSystem("");
                  setPage(1);
                  setRefresh((x) => x + 1);
                }}
              >
                Restablecer filtros
              </button>
            </div>
          )}
          {semanticOpen && ["documents", "reviews"].includes(tab) && (
            <section className="panel semantic-panel">
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  action(async () => {
                    const r = await api(
                      "/semantic/ask?" + new URLSearchParams({ question }),
                    );
                    setSemanticResults(r.results);
                  });
                }}
              >
                <label>
                  Pregunta sobre un procedimiento
                  <input
                    value={question}
                    onChange={(e) => setQuestion(e.target.value)}
                    placeholder="¿Cómo se cambia el filtro hidráulico?"
                    required
                    maxLength={1000}
                  />
                </label>
                <button className="button primary" disabled={busy}>
                  Consultar
                </button>
              </form>
              {semanticResults.map((r, i) => (
                <button
                  className="semantic-result"
                  key={i}
                  onClick={() => openDocument(r.documentId)}
                >
                  <strong>
                    {r.code} · {r.title} · v{r.version} / paso {r.step}
                  </strong>
                  <p>{r.citation}</p>
                </button>
              ))}
            </section>
          )}
          {["documents", "reviews"].includes(tab) && (
            <>
              <div className="list-summary">
                <span>
                  {docs.total} documentos{" "}
                  {selected.length > 0 && `· ${selected.length} seleccionados`}
                </span>
                <div>
                  {selected.length > 0 && (
                    <button className="button secondary" onClick={exportPdf}>
                      Exportar selección
                    </button>
                  )}
                  <select
                    aria-label="Ordenar página"
                    value={order}
                    onChange={(e) => setOrder(e.target.value)}
                  >
                    <option value="recent">Modificados recientemente</option>
                    <option value="title">Título (A–Z)</option>
                    <option value="code">Referencia</option>
                  </select>
                </div>
              </div>
              {docCards(docs.items)}
            </>
          )}
          {tab === "parts" && (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Referencia / pieza</th>
                    <th>Sistema</th>
                    <th>Proveedor</th>
                    <th>Revisión</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {parts.items.map((p: Item) => (
                    <tr key={p.id}>
                      <td>
                        <button
                          className="row-link"
                          onClick={() =>
                            action(async () =>
                              setPartDetail(await api("/parts/" + p.id)),
                            )
                          }
                        >
                          <span className="eyebrow">{p.code}</span>
                          <strong>{p.name}</strong>
                        </button>
                      </td>
                      <td>{p.system}</td>
                      <td>{p.supplier}</td>
                      <td>
                        <span className="version-chip">R{p.revision}</span>
                      </td>
                      <td>
                        <button
                          aria-label={"Ver " + p.code}
                          onClick={() =>
                            action(async () =>
                              setPartDetail(await api("/parts/" + p.id)),
                            )
                          }
                        >
                          <ArrowUpRight size={18} />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {!parts.items.length && (
                <div className="empty">No se encontraron piezas.</div>
              )}
            </div>
          )}
          {tab === "reviews" && (
            <section className="panel task-panel">
              <div className="section-top">
                <h2>Revisiones por impacto</h2>
                <span className="subtle">
                  {tasks.filter((t) => !t.completed).length} pendientes
                </span>
              </div>
              {tasks.length ? (
                tasks.map((t) => (
                  <div className="task" key={t.id}>
                    <ClipboardCheck size={21} />
                    <div>
                      <button
                        className="row-link"
                        onClick={() => openDocument(t.documentId)}
                      >
                        <strong>{t.document}</strong>
                      </button>
                      <p>{t.reason}</p>
                    </div>
                    {t.completed ? (
                      <span className="badge Approved">Completada</span>
                    ) : (
                      ["Revisor", "Aprobador"].includes(role) && (
                        <button
                          className="button secondary"
                          onClick={() =>
                            action(async () => {
                              await api("/tasks/" + t.id + "/complete", {
                                method: "POST",
                              });
                              setRefresh((x) => x + 1);
                            })
                          }
                        >
                          Completar
                        </button>
                      )
                    )}
                  </div>
                ))
              ) : (
                <div className="empty compact">
                  No hay tareas de impacto. Aparecerán al modificar una pieza
                  vinculada.
                </div>
              )}
            </section>
          )}
          {tab === "imports" && (
            <>
              <section className="upload-panel">
                <span className="upload-icon">
                  <Upload size={30} />
                </span>
                <h2>Importar catálogo de piezas</h2>
                <p>
                  Importa un Excel o XML. El proceso valida, limpia y detecta
                  duplicados antes de guardar.
                </p>
                <label
                  className={
                    "button primary " +
                    (busy || role !== "Redactor" ? "disabled" : "")
                  }
                >
                  <Upload size={17} />
                  Seleccionar archivo
                  <input
                    type="file"
                    accept=".xlsx,.xml"
                    disabled={busy || role !== "Redactor"}
                    onChange={(e) => {
                      const f = e.target.files?.[0];
                      if (f)
                        action(async () => {
                          const form = new FormData();
                          form.append("file", f);
                          await api("/imports", { method: "POST", body: form });
                          setRefresh((x) => x + 1);
                          setNotice(
                            "Importación encolada. El informe se actualizará automáticamente.",
                          );
                        });
                      e.target.value = "";
                    }}
                  />
                </label>
                <small>
                  Excel o XML · Máximo 10 MB / 50.000 filas · Solo Redactor
                </small>
                <a href="/samples/catalogo-ejemplo.xlsx" download>
                  Descargar plantilla de ejemplo <Download size={13} />
                </a>
              </section>
              <section className="panel">
                <div className="section-top">
                  <h2>Historial de importaciones</h2>
                  <button
                    className="text-button"
                    onClick={() => setRefresh((x) => x + 1)}
                  >
                    Actualizar
                    <History size={15} />
                  </button>
                </div>
                {imports.length ? (
                  imports.map((i) => (
                    <div className="import-entry" key={i.id}>
                      <div className="import-row">
                        <span className="file-icon">
                          <FileText size={21} />
                        </span>
                        <div>
                          <strong>{i.fileName}</strong>
                          <p>{date(i.createdAt)}</p>
                        </div>
                        <span
                          className={
                            "badge " +
                            (i.status === "Completed" ? "Approved" : "InReview")
                          }
                        >
                          {(
                            {
                              Queued: "En cola",
                              Processing: "Procesando",
                              Completed: "Completada",
                            } as Item
                          )[i.status] || i.status}
                        </span>
                        <span>
                          {i.accepted} correctas / {i.rejected} errores
                        </span>
                      </div>
                      {JSON.parse(i.issues).length > 0 && (
                        <details>
                          <summary>Ver filas rechazadas</summary>
                          <table>
                            <thead>
                              <tr>
                                <th>Fila</th>
                                <th>Código</th>
                                <th>Motivo</th>
                              </tr>
                            </thead>
                            <tbody>
                              {JSON.parse(i.issues).map((r: Item) => (
                                <tr key={r.Row}>
                                  <td>{r.Row}</td>
                                  <td>{r.Code}</td>
                                  <td>{r.Error}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </details>
                      )}
                    </div>
                  ))
                ) : (
                  <div className="empty compact">
                    Todavía no hay importaciones. Empieza con el archivo de
                    ejemplo.
                  </div>
                )}
              </section>
            </>
          )}
          {tab === "audit" && (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Fecha</th>
                    <th>Acción</th>
                    <th>Usuario</th>
                    <th>Detalle</th>
                  </tr>
                </thead>
                <tbody>
                  {audit.items.map((a: Item) => (
                    <tr key={a.id}>
                      <td className="nowrap">{date(a.at)}</td>
                      <td>
                        <span className="version-chip">{a.action}</span>
                      </td>
                      <td className="mono">{a.actor.slice(0, 8)}</td>
                      <td className="audit-detail">{a.detail}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {["documents", "reviews", "parts", "audit"].includes(tab) && (
            <div className="pagination">
              <span>Página {page}</span>
              <button
                className="button secondary"
                disabled={page === 1}
                onClick={() => setPage(page - 1)}
              >
                Anterior
              </button>
              <button
                className="button secondary"
                disabled={
                  page * (tab === "parts" ? 20 : tab === "audit" ? 30 : 12) >=
                  (tab === "parts"
                    ? parts.total
                    : tab === "audit"
                      ? audit.total
                      : docs.total)
                }
                onClick={() => setPage(page + 1)}
              >
                Siguiente
              </button>
            </div>
          )}
          <footer className="main-footer">
            <span>TechDocs / Atlas R-240</span>
            <span>
              {dashboard.mode} <span className="dot" />
            </span>
          </footer>
        </main>
      </div>
      {detail && !editor && (
        <div className="modal-backdrop" onClick={() => setDetail(null)}>
          <section
            className="document-modal"
            role="dialog"
            aria-modal="true"
            aria-label="Detalle del documento"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="modal-header">
              <span className="eyebrow">
                {detail.code} / Versión {detail.version}
              </span>
              <button
                aria-label="Cerrar documento"
                onClick={() => setDetail(null)}
              >
                <X />
              </button>
            </div>
            <h2>{detail.title}</h2>
            <div className="document-meta">
              {statusBadge(detail.state)}
              <span>{detail.system}</span>
              <span>Actualizado {date(detail.updatedAt)}</span>
            </div>
            <div className="modal-actions">
              <select
                aria-label="Seleccionar versión"
                value={detail.version}
                onChange={(e) =>
                  openDocument(detail.id, Number(e.target.value))
                }
              >
                {detail.versions.map((v: Item) => (
                  <option key={v.number} value={v.number}>
                    Versión {v.number} · {states[v.state]}
                  </option>
                ))}
              </select>
              {detail.versions.length > 1 && (
                <>
                  <select
                    aria-label="Versión para comparar"
                    value={diffFrom}
                    onChange={(e) => setDiffFrom(Number(e.target.value))}
                  >
                    {detail.versions.map((v: Item) => (
                      <option value={v.number} key={v.number}>
                        Comparar con v{v.number}
                      </option>
                    ))}
                  </select>
                  <button
                    className="button secondary"
                    onClick={() =>
                      action(async () =>
                        setDiff(
                          await api(
                            `/documents/${detail.id}/diff?from=${diffFrom}&to=${detail.version}`,
                          ),
                        ),
                      )
                    }
                  >
                    Ver cambios
                  </button>
                </>
              )}
              {role === "Redactor" &&
                detail.version === detail.currentVersion &&
                detail.state !== "InReview" && (
                  <button
                    className="button secondary"
                    onClick={() =>
                      setEditor({
                        ...detail,
                        partIds: detail.parts.map((p: Item) => p.id),
                        blocks: detail.blocks.map((b: Block) => ({ ...b })),
                      })
                    }
                  >
                    Crear nueva versión
                  </button>
                )}
            </div>
            {diff ? (
              <div className="diff-view">
                <div className="section-top">
                  <h3>
                    Comparación v{diff.from.number} → v{diff.to.number}
                  </h3>
                  <button className="text-button" onClick={() => setDiff(null)}>
                    Cerrar comparación
                  </button>
                </div>
                {diffLines(
                  diff.from.blocks?.map((b: Block) => b.text).join("\n") ||
                    diff.from.Blocks?.map((b: Item) => b.Text).join("\n") ||
                    "",
                  diff.to.blocks?.map((b: Block) => b.text).join("\n") ||
                    diff.to.Blocks?.map((b: Item) => b.Text).join("\n") ||
                    "",
                ).map((p, i) => (
                  <pre
                    key={i}
                    className={p.added ? "added" : p.removed ? "removed" : ""}
                  >
                    {p.added ? "+ " : p.removed ? "− " : "  "}
                    {p.value}
                  </pre>
                ))}
              </div>
            ) : (
              <div className="blocks">
                {detail.blocks.map((b: Block, i: number) => (
                  <div key={i} className={"block " + b.type}>
                    <span className="block-label">
                      {
                        (
                          {
                            heading: "Sección",
                            step:
                              "Paso " +
                              detail.blocks
                                .slice(0, i + 1)
                                .filter((x: Block) => x.type === "step").length,
                            warning: "Advertencia",
                            note: "Nota",
                            table: "Tabla",
                            image: "Imagen",
                          } as Item
                        )[b.type]
                      }
                    </span>
                    {b.type === "table" ? (
                      <table>
                        <tbody>
                          {b.text.split("\n").map((row, j) => (
                            <tr key={j}>
                              {row
                                .split("|")
                                .map((c, k) =>
                                  j === 0 ? (
                                    <th key={k}>{c}</th>
                                  ) : (
                                    <td key={k}>{c}</td>
                                  ),
                                )}
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    ) : b.type === "image" ? (
                      <a href={b.text} target="_blank" rel="noreferrer">
                        Abrir imagen de referencia
                        <ArrowUpRight size={13} />
                      </a>
                    ) : (
                      <p>{b.text}</p>
                    )}
                    {b.partCode && <small>Referencia: {b.partCode}</small>}
                  </div>
                ))}
              </div>
            )}
            <div className="linked-parts">
              <span className="eyebrow">Piezas vinculadas</span>
              {detail.parts.map((p: Item) => (
                <span className="version-chip" key={p.id}>
                  {p.code} · {p.name}
                </span>
              ))}
            </div>
            {detail.version === detail.currentVersion && (
              <div className="workflow-actions">
                {role === "Redactor" && detail.state === "Draft" && (
                  <button
                    className="button primary"
                    disabled={busy}
                    onClick={() => transition("submit")}
                  >
                    Enviar a revisión
                    <ArrowRight size={16} />
                  </button>
                )}
                {role === "Revisor" && detail.state === "InReview" && (
                  <>
                    <button
                      className="button primary"
                      disabled={busy || !!detail.reviewedBy}
                      onClick={() => transition("review")}
                    >
                      {detail.reviewedBy
                        ? "Revisión registrada"
                        : "Validar revisión"}
                    </button>
                    <input
                      aria-label="Motivo de devolución"
                      placeholder="Motivo de devolución"
                      value={reason}
                      onChange={(e) => setReason(e.target.value)}
                    />
                    <button
                      className="button secondary"
                      disabled={busy || !reason.trim()}
                      onClick={() => transition("reject")}
                    >
                      Devolver a borrador
                    </button>
                  </>
                )}
                {role === "Aprobador" && detail.state === "InReview" && (
                  <button
                    className="button primary"
                    disabled={busy || !detail.reviewedBy}
                    onClick={() => transition("approve")}
                  >
                    <Check size={16} />
                    Aprobar documento
                  </button>
                )}
                {role === "Aprobador" && detail.state === "Approved" && (
                  <button
                    className="button secondary"
                    disabled={busy}
                    onClick={() => transition("obsolete")}
                  >
                    Marcar como obsoleto
                  </button>
                )}
              </div>
            )}
          </section>
        </div>
      )}
      {editor && (
        <div className="modal-backdrop">
          <section
            className="document-modal editor"
            role="dialog"
            aria-modal="true"
            aria-label="Editor de documento"
          >
            <div className="modal-header">
              <span className="eyebrow">
                {editor.id ? "Nueva versión" : "Nuevo documento"}
              </span>
              <button
                aria-label="Cerrar editor"
                onClick={() => setEditor(null)}
              >
                <X />
              </button>
            </div>
            <h2>Editor de procedimiento</h2>
            <div className="form-grid">
              <label>
                Código
                <input
                  value={editor.code}
                  disabled={!!editor.id}
                  placeholder="MP-013"
                  onChange={(e) =>
                    setEditor({ ...editor, code: e.target.value.toUpperCase() })
                  }
                />
              </label>
              <label>
                Sistema
                <select
                  value={editor.systemId}
                  onChange={(e) =>
                    setEditor({ ...editor, systemId: e.target.value })
                  }
                >
                  {systems.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </select>
              </label>
              <label className="full">
                Título
                <input
                  value={editor.title}
                  onChange={(e) =>
                    setEditor({ ...editor, title: e.target.value })
                  }
                  placeholder="Título del procedimiento"
                />
              </label>
            </div>
            <h3>Contenido por bloques</h3>
            {editor.blocks.map((b: Block, i: number) => (
              <div className="editor-block" key={i}>
                <div>
                  <span className="subtle">
                    {String(i + 1).padStart(2, "0")}
                  </span>
                  <select
                    aria-label={"Tipo de bloque " + (i + 1)}
                    value={b.type}
                    onChange={(e) => {
                      const bs = [...editor.blocks];
                      bs[i] = { ...b, type: e.target.value };
                      setEditor({ ...editor, blocks: bs });
                    }}
                  >
                    {[
                      ["heading", "Sección"],
                      ["step", "Paso"],
                      ["warning", "Advertencia"],
                      ["note", "Nota"],
                      ["table", "Tabla (columnas con |)"],
                      ["image", "Imagen (enlace HTTPS)"],
                    ].map(([v, l]) => (
                      <option key={v} value={v}>
                        {l}
                      </option>
                    ))}
                  </select>
                  <button
                    aria-label={"Eliminar bloque " + (i + 1)}
                    onClick={() =>
                      setEditor({
                        ...editor,
                        blocks: editor.blocks.filter(
                          (_: Block, j: number) => i !== j,
                        ),
                      })
                    }
                  >
                    <Trash2 size={17} />
                  </button>
                </div>
                <textarea
                  aria-label={"Contenido del bloque " + (i + 1)}
                  rows={3}
                  value={b.text}
                  onChange={(e) => {
                    const bs = [...editor.blocks];
                    bs[i] = { ...b, text: e.target.value };
                    setEditor({ ...editor, blocks: bs });
                  }}
                />
              </div>
            ))}
            <button
              className="button secondary"
              onClick={() =>
                setEditor({
                  ...editor,
                  blocks: [...editor.blocks, { type: "step", text: "" }],
                })
              }
            >
              <Plus size={16} />
              Añadir bloque
            </button>
            <label className="part-selector">
              Vincular piezas
              <select
                multiple
                aria-label="Vincular piezas"
                value={editor.partIds}
                onChange={(e) =>
                  setEditor({
                    ...editor,
                    partIds: Array.from(
                      e.target.selectedOptions,
                      (x) => x.value,
                    ),
                  })
                }
              >
                {parts.items.map((p: Item) => (
                  <option key={p.id} value={p.id}>
                    {p.code} · {p.name}
                  </option>
                ))}
              </select>
              <small>
                Usa ⌘ o Ctrl para seleccionar varias. Se muestran las piezas de
                la página actual del catálogo.
              </small>
            </label>
            <div className="workflow-actions">
              <button
                className="button secondary"
                onClick={() => setEditor(null)}
              >
                Cancelar
              </button>
              <button className="button primary" disabled={busy} onClick={save}>
                <Check size={16} />
                Guardar versión
              </button>
            </div>
          </section>
        </div>
      )}
      {partDetail && (
        <div className="modal-backdrop" onClick={() => setPartDetail(null)}>
          <section
            className="document-modal part-modal"
            role="dialog"
            aria-modal="true"
            aria-label="Ficha de pieza"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="modal-header">
              <span className="eyebrow">
                {partDetail.code} / Revisión {partDetail.revision}
              </span>
              <button
                aria-label="Cerrar ficha"
                onClick={() => setPartDetail(null)}
              >
                <X />
              </button>
            </div>
            <h2>{partDetail.name}</h2>
            <p className="subtle">
              {partDetail.system} · {partDetail.supplier}
            </p>
            <div className="form-grid">
              <label className="full">
                Nombre
                <input
                  disabled={role !== "Redactor"}
                  value={partDetail.name}
                  onChange={(e) =>
                    setPartDetail({ ...partDetail, name: e.target.value })
                  }
                />
              </label>
              <label className="full">
                Especificación
                <textarea
                  disabled={role !== "Redactor"}
                  rows={4}
                  value={partDetail.specification}
                  onChange={(e) =>
                    setPartDetail({
                      ...partDetail,
                      specification: e.target.value,
                    })
                  }
                />
              </label>
            </div>
            <h3>Documentos afectados ({partDetail.documents.length})</h3>
            {partDetail.documents.map((d: Item) => (
              <button
                className="affected-doc"
                key={d.documentId}
                onClick={() => {
                  setPartDetail(null);
                  openDocument(d.documentId);
                }}
              >
                <FileText size={18} />
                <span>
                  {d.code} · {d.title}
                </span>
                <ArrowUpRight size={16} />
              </button>
            ))}
            {role === "Redactor" && (
              <div className="workflow-actions">
                <p>
                  Al guardar se crearán tareas de revisión para los documentos
                  vinculados.
                </p>
                <button
                  disabled={busy}
                  className="button primary"
                  onClick={() =>
                    action(async () => {
                      const r = await api("/parts/" + partDetail.id, {
                        method: "PUT",
                        body: JSON.stringify({
                          name: partDetail.name,
                          specification: partDetail.specification,
                          expectedRevision: partDetail.revision,
                        }),
                      });
                      setPartDetail(await api("/parts/" + partDetail.id));
                      setNotice(
                        `Pieza actualizada. ${r.affectedDocuments.length} documentos requieren revisión.`,
                      );
                      setRefresh((x) => x + 1);
                    })
                  }
                >
                  Guardar y analizar impacto
                </button>
              </div>
            )}
          </section>
        </div>
      )}
    </div>
  );
}
createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
