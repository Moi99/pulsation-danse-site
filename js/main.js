const body = document.body;
document.documentElement.classList.add("js-enabled");

const header = document.querySelector(".site-header");
const menuToggle = document.querySelector(".menu-toggle");
const siteNav = document.querySelector(".site-nav");
const navLinks = document.querySelectorAll(".site-nav a");
const yearTargets = document.querySelectorAll("[data-year]");
const form = document.querySelector("[data-placeholder-form]");
const formNote = document.querySelector("[data-form-note]");
const glowSurfaces = document.querySelectorAll(".hero, .section--dark, .site-footer");
const videoModal = document.querySelector("[data-video-modal]");
const videoTriggers = document.querySelectorAll("[data-video-trigger]");
const videoCloseControls = document.querySelectorAll("[data-video-close]");
const videoStyleName = document.querySelector("[data-video-style-name]");
const videoPlaceholderLabel = document.querySelector("[data-video-placeholder-label]");
const videoModalTitle = document.querySelector("#video-modal-title");
const videoModalCloseButton = document.querySelector(".video-modal__close");
const bioModal = document.querySelector("[data-bio-modal]");
const bioTriggers = document.querySelectorAll("[data-bio-trigger]");
const bioCloseControls = document.querySelectorAll("[data-bio-close]");
const bioModalTitle = document.querySelector("#bio-modal-title");
const bioModalText = document.querySelector("[data-bio-modal-text]");
const bioModalCloseButton = document.querySelector(".bio-modal__close");
const facebookEventBoards = document.querySelectorAll("[data-facebook-events]");
const venuePopovers = document.querySelectorAll("[data-venue-popover]");
const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
const lerp = (start, end, amount) => start + (end - start) * amount;
const parseLocalDate = (value) => {
  if (!value) {
    return null;
  }

  const [year, month, day] = value.split("-").map(Number);

  if (![year, month, day].every(Number.isFinite)) {
    return null;
  }

  return new Date(year, month - 1, day, 12);
};
let lastVideoTrigger = null;
let lastBioTrigger = null;

const closeMenu = () => {
  if (!menuToggle || !siteNav) {
    return;
  }

  document.documentElement.classList.remove("nav-open");
  body.classList.remove("nav-open");
  menuToggle.setAttribute("aria-expanded", "false");
};

if (menuToggle && siteNav) {
  menuToggle.addEventListener("click", () => {
    const isOpen = body.classList.toggle("nav-open");
    document.documentElement.classList.toggle("nav-open", isOpen);
    menuToggle.setAttribute("aria-expanded", String(isOpen));
  });

  navLinks.forEach((link) => {
    link.addEventListener("click", closeMenu);
  });

  window.addEventListener("resize", () => {
    if (window.innerWidth > 860) {
      closeMenu();
    }
  });
}

if (header) {
  const syncHeader = () => {
    header.classList.toggle("is-scrolled", window.scrollY > 12);
  };

  syncHeader();
  window.addEventListener("scroll", syncHeader, { passive: true });
}

yearTargets.forEach((target) => {
  target.textContent = new Date().getFullYear();
});

if (venuePopovers.length) {
  const closeVenuePopovers = (except = null) => {
    venuePopovers.forEach((popover) => {
      if (popover === except) {
        return;
      }

      const trigger = popover.querySelector(".venue-popover__trigger");
      const panel = popover.querySelector(".venue-popover__panel");

      if (trigger && panel) {
        trigger.setAttribute("aria-expanded", "false");
        panel.hidden = true;
      }
    });
  };

  venuePopovers.forEach((popover) => {
    const trigger = popover.querySelector(".venue-popover__trigger");
    const panel = popover.querySelector(".venue-popover__panel");

    if (!trigger || !panel) {
      return;
    }

    trigger.addEventListener("click", (event) => {
      event.stopPropagation();

      const isOpen = trigger.getAttribute("aria-expanded") === "true";
      closeVenuePopovers(popover);
      trigger.setAttribute("aria-expanded", String(!isOpen));
      panel.hidden = isOpen;
    });

    popover.addEventListener("click", (event) => {
      event.stopPropagation();
    });
  });

  document.addEventListener("click", () => closeVenuePopovers());
  window.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      closeVenuePopovers();
    }
  });
}

document.querySelectorAll("[data-session-calendar]").forEach((calendar) => {
  const sessionCards = calendar.querySelectorAll("[data-session-start]");
  const durationDays = Number.parseInt(calendar.dataset.sessionDurationDays || "49", 10);

  if (!sessionCards.length || Number.isNaN(durationDays)) {
    return;
  }

  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 12);

  sessionCards.forEach((card) => {
    const startDate = parseLocalDate(card.dataset.sessionStart);

    if (!startDate || Number.isNaN(startDate.getTime())) {
      return;
    }

    const endDate = new Date(startDate);
    endDate.setDate(endDate.getDate() + durationDays);

    let state = "upcoming";
    let label = "\u00c0 venir";

    if (today >= endDate) {
      state = "complete";
      label = "Termin\u00e9e";
    } else if (today >= startDate) {
      state = "current";
      label = "En cours";
    }

    card.dataset.sessionState = state;
    card.classList.remove("is-upcoming", "is-current", "is-complete");
    card.classList.add(`is-${state}`);

    const status = card.querySelector("[data-session-status]");

    if (status) {
      status.textContent = label;
    }
  });
});

if (facebookEventBoards.length) {
  const eventTypes = ["session", "soiree-locale", "weekender"];
  const eventDanceGroups = [
    { slug: "wcs", label: "WCS", aliases: ["wcs", "west-coast-swing"] },
    { slug: "zouk", label: "Zouk", aliases: ["zouk", "zouk-bresilien"] },
    { slug: "blues", label: "Blues", aliases: ["blues"] }
  ];
  const eventDateFormatter = new Intl.DateTimeFormat("fr-CA", {
    day: "numeric",
    month: "long",
    year: "numeric"
  });

  const slugify = (value) =>
    String(value || "")
      .trim()
      .toLowerCase()
      .normalize("NFD")
      .replace(/[\u0300-\u036f]/g, "")
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "");

  const createTextElement = (tagName, className, text) => {
    const element = document.createElement(tagName);
    element.className = className;
    element.textContent = text;
    return element;
  };

  const getEventDances = (event) => {
    if (Array.isArray(event.dance)) {
      return event.dance;
    }

    if (Array.isArray(event.dances)) {
      return event.dances;
    }

    if (event.dance) {
      return [event.dance];
    }

    return [];
  };

  const getEventType = (event) => {
    const type = slugify(event.type || "soiree-locale");

    if (type === "soiree" || type === "soiree-locale" || type === "local") {
      return "soiree-locale";
    }

    if (type === "session") {
      return "session";
    }

    if (type === "weekend" || type === "weekender") {
      return "weekender";
    }

    return eventTypes.includes(type) ? type : "soiree-locale";
  };

  const getEventDanceSlugs = (event) => {
    const dances = getEventDances(event).map(slugify);
    const matches = eventDanceGroups
      .filter((group) => dances.some((dance) => dance === group.slug || group.aliases.includes(dance)))
      .map((group) => group.slug);

    return matches.length ? matches : ["wcs"];
  };

  const getEventDanceLabel = (danceSlug) =>
    eventDanceGroups.find((group) => group.slug === danceSlug)?.label || String(danceSlug || "").toUpperCase();

  const getEventStartTime = (event) => {
    const startDate = parseLocalDate(event.date);

    return startDate && !Number.isNaN(startDate.getTime()) ? startDate.getTime() : Number.MAX_SAFE_INTEGER;
  };

  const getEventSelection = (selectionKey) => {
    const [danceValue, typeValue] = String(selectionKey || "").split(":");
    const dance = slugify(danceValue);

    if (!eventDanceGroups.some((group) => group.slug === dance)) {
      return null;
    }

    const type = typeValue ? getEventType({ type: typeValue }) : null;

    return {
      dance,
      type,
      key: type ? `${dance}:${type}` : dance
    };
  };

  const getEventSortRank = (event, selection) => {
    if (!selection) {
      return 0;
    }

    if (!getEventDanceSlugs(event).includes(selection.dance)) {
      return 2;
    }

    if (!selection.type) {
      return 0;
    }

    return getEventType(event) === selection.type ? 0 : 1;
  };

  const sortEventItems = (items, selection) =>
    items.slice().sort((first, second) => {
      const rankDifference = getEventSortRank(first.event, selection) - getEventSortRank(second.event, selection);

      if (rankDifference) {
        return rankDifference;
      }

      const dateDifference = getEventStartTime(first.event) - getEventStartTime(second.event);

      if (dateDifference) {
        return dateDifference;
      }

      return String(first.event.title || "").localeCompare(String(second.event.title || ""), "fr");
    });

  const formatEventType = (type) => {
    if (type === "session") {
      return "Session";
    }

    if (type === "weekender") {
      return "Weekender";
    }

    return "Soirée locale";
  };

  const formatEventDateRange = (event) => {
    const startDate = parseLocalDate(event.date);
    const endDate = parseLocalDate(event.endDate);

    if (!startDate || Number.isNaN(startDate.getTime())) {
      return "Date à confirmer";
    }

    if (endDate && !Number.isNaN(endDate.getTime()) && endDate > startDate) {
      return `${eventDateFormatter.format(startDate)} au ${eventDateFormatter.format(endDate)}`;
    }

    return eventDateFormatter.format(startDate);
  };

  const getEventState = (event, today) => {
    const startDate = parseLocalDate(event.date);
    const endDate = parseLocalDate(event.endDate || event.date);

    if (!startDate || !endDate || Number.isNaN(startDate.getTime()) || Number.isNaN(endDate.getTime())) {
      return "upcoming";
    }

    if (endDate < today) {
      return "archived";
    }

    if (startDate <= today && today <= endDate) {
      return "current";
    }

    return "upcoming";
  };

  const addEventPlaceholder = (media, label) => {
    media.replaceChildren(createTextElement("span", "event-card__placeholder", label || "Événement"));
  };

  const createEventCard = (event, state) => {
    const card = document.createElement(event.url ? "a" : "article");
    const title = event.title || "Événement à nommer";
    const dances = getEventDances(event).filter(Boolean);
    const type = getEventType(event);
    const danceSlugs = getEventDanceSlugs(event);
    const badgeDanceText = danceSlugs.map(getEventDanceLabel).join(" / ");
    const metaParts = [formatEventDateRange(event), event.time, event.location].filter(Boolean);

    card.className = `event-card is-${state}`;

    if (event.url) {
      card.href = event.url;
      card.target = "_blank";
      card.rel = "noopener noreferrer";
      card.setAttribute("aria-label", `${title} - ouvrir l'événement Facebook`);
    }

    const media = document.createElement("div");
    media.className = "event-card__media";

    if (event.image) {
      const image = document.createElement("img");
      image.src = event.image;
      image.alt = event.imageAlt || "";
      image.loading = "lazy";
      image.addEventListener("error", () => addEventPlaceholder(media, badgeDanceText || dances[0] || "Événement"));
      media.append(image);
    } else {
      addEventPlaceholder(media, badgeDanceText || dances[0] || "Événement");
    }

    const bodyContent = document.createElement("div");
    bodyContent.className = "event-card__body";

    const badges = document.createElement("div");
    badges.className = "event-card__badges";

    badges.append(createTextElement("span", `event-badge event-badge--${danceSlugs[0]}`, `${formatEventType(type)} - ${badgeDanceText}`));

    if (state === "current") {
      badges.append(createTextElement("span", "event-badge", "En cours"));
    }

    if (badges.children.length) {
      bodyContent.append(badges);
    }

    bodyContent.append(createTextElement("h4", "event-card__title", title));
    bodyContent.append(createTextElement("p", "event-card__meta", metaParts.join(" · ")));

    if (event.url) {
      bodyContent.append(createTextElement("span", "event-card__link", "Voir sur Facebook"));
    }

    card.append(media, bodyContent);
    return card;
  };

  facebookEventBoards.forEach((board) => {
    const source = board.dataset.eventsSource;
    const activeList = board.querySelector("[data-event-list='active']");
    const listSection = board.querySelector("#events-active-list") || board.querySelector("#liste-evenements");
    const listTitle = board.querySelector("[data-event-list-title]");
    const archiveCta = board.querySelector("[data-event-archive-cta]");
    const archiveToggle = board.querySelector("[data-event-archive-toggle]");
    const directoryEmpty = board.querySelector("[data-event-directory-empty]");
    const countTargets = new Map(
      Array.from(board.querySelectorAll("[data-event-count]")).map((target) => [target.dataset.eventCount, target])
    );
    const categoryLinks = board.querySelectorAll("[data-event-category-link]");

    if (!source || !activeList) {
      return;
    }

    activeList.replaceChildren();
    countTargets.forEach((target) => {
      target.textContent = "0";
    });

    if (archiveCta) {
      archiveCta.hidden = true;
    }

    if (directoryEmpty) {
      directoryEmpty.hidden = true;
    }

    const embeddedEvents = window.PULSATION_EVENTS && typeof window.PULSATION_EVENTS === "object"
      ? window.PULSATION_EVENTS
      : null;
    const sourceUrl = `${source}${source.includes("?") ? "&" : "?"}v=${Date.now()}`;

    fetch(sourceUrl, { cache: "no-store" })
      .then((response) => {
        if (!response.ok) {
          throw new Error(`Impossible de charger ${sourceUrl}`);
        }

        return response.json();
      })
      .catch((error) => {
        if (embeddedEvents) {
          return embeddedEvents;
        }

        throw error;
      })
      .then((data) => {
        const now = new Date();
        const today = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 12);
        const events = Array.isArray(data.events) ? data.events.filter((event) => event && typeof event === "object") : [];
        const activeCounts = Object.fromEntries(eventTypes.map((type) => [type, 0]));
        const activeEventItems = [];
        const archivedEventItems = [];
        let activeSelection = null;
        let isShowingArchives = false;

        events
          .slice()
          .sort((first, second) => {
            const firstDate = parseLocalDate(first.date);
            const secondDate = parseLocalDate(second.date);
            return (firstDate?.getTime() || 0) - (secondDate?.getTime() || 0);
          })
          .forEach((event) => {
            const state = getEventState(event, today);
            const type = getEventType(event);
            const danceSlugs = getEventDanceSlugs(event);

            if (state === "archived") {
              archivedEventItems.push({ event, state });
              return;
            }

            activeEventItems.push({ event, state });
            activeCounts[type] += 1;
            danceSlugs.forEach((dance) => {
              activeCounts[dance] = (activeCounts[dance] || 0) + 1;
              activeCounts[`${dance}:${type}`] = (activeCounts[`${dance}:${type}`] || 0) + 1;
            });
          });

        const renderActiveEvents = (selection = null) => {
          activeSelection = selection;
          isShowingArchives = false;
          activeList.replaceChildren();

          sortEventItems(activeEventItems, activeSelection).forEach(({ event, state }) => {
            activeList.append(createEventCard(event, state));
          });

          if (listTitle) {
            listTitle.textContent = "Événements en cours et à venir.";
          }

          if (directoryEmpty) {
            directoryEmpty.hidden = activeEventItems.length > 0;
            directoryEmpty.textContent = "Aucun événement actif à afficher pour le moment.";
          }

          if (archiveToggle) {
            archiveToggle.setAttribute("aria-pressed", "false");
            archiveToggle.textContent = "Voir les archives";
          }

          categoryLinks.forEach((link) => {
            const linkSelection = getEventSelection(link.dataset.eventCategoryLink);
            const isCurrent = linkSelection && activeSelection && linkSelection.key === activeSelection.key;

            if (isCurrent) {
              link.setAttribute("aria-current", "true");
            } else {
              link.removeAttribute("aria-current");
            }
          });
        };

        const renderArchivedEvents = () => {
          activeSelection = null;
          isShowingArchives = true;
          activeList.replaceChildren();

          sortEventItems(archivedEventItems, null).forEach(({ event, state }) => {
            activeList.append(createEventCard(event, state));
          });

          if (listTitle) {
            listTitle.textContent = "Événements archivés.";
          }

          if (directoryEmpty) {
            directoryEmpty.hidden = archivedEventItems.length > 0;
            directoryEmpty.textContent = "Aucun événement archivé à afficher pour le moment.";
          }

          if (archiveToggle) {
            archiveToggle.setAttribute("aria-pressed", "true");
            archiveToggle.textContent = "Événements courants";
          }

          categoryLinks.forEach((link) => {
            link.removeAttribute("aria-current");
          });
        };

        countTargets.forEach((target, type) => {
          target.textContent = String(activeCounts[type] || 0);
        });

        categoryLinks.forEach((link) => {
          const selection = getEventSelection(link.dataset.eventCategoryLink);

          link.href = "#events-active-list";
          link.removeAttribute("aria-disabled");
          link.addEventListener("click", (event) => {
            event.preventDefault();

            renderActiveEvents(selection);
            listSection?.scrollIntoView({
              behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth",
              block: "start"
            });
          });
        });

        renderActiveEvents();

        if (archiveCta) {
          archiveCta.hidden = false;
        }

        if (archiveToggle) {
          archiveToggle.addEventListener("click", () => {
            if (isShowingArchives) {
              renderActiveEvents();
            } else {
              renderArchivedEvents();
            }

            listSection?.scrollIntoView({
              behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth",
              block: "start"
            });
          });
        }
      })
      .catch(() => {
        activeList.replaceChildren();

        if (directoryEmpty) {
          directoryEmpty.hidden = false;
          directoryEmpty.textContent = "Impossible de charger les événements pour le moment.";
        }
      });
  });
}

if (form && formNote) {
  form.addEventListener("submit", (event) => {
    event.preventDefault();
    formNote.textContent = "Le formulaire de cette V1 est en attente de l'adresse courriel finale et de son integration.";
  });
}

const closeBioModal = () => {
  if (!bioModal) {
    return;
  }

  bioModal.hidden = true;
  body.classList.remove("modal-open");

  if (lastBioTrigger) {
    lastBioTrigger.focus();
  }
};

if (bioModal && bioTriggers.length) {
  const openBioModal = (trigger) => {
    const fullBio = trigger.querySelector("[data-bio-full]")?.textContent?.trim();
    const previewBio = trigger.querySelector("[data-bio-preview]")?.textContent?.trim();

    if (bioModalTitle) {
      bioModalTitle.textContent = trigger.dataset.bioTitle || "Bio";
    }

    if (bioModalText) {
      bioModalText.textContent = fullBio || previewBio || "";
    }

    lastBioTrigger = trigger;
    bioModal.hidden = false;
    body.classList.add("modal-open");

    if (bioModalCloseButton) {
      bioModalCloseButton.focus();
    }
  };

  bioTriggers.forEach((trigger) => {
    trigger.addEventListener("click", () => openBioModal(trigger));
  });

  bioCloseControls.forEach((control) => {
    control.addEventListener("click", closeBioModal);
  });

  window.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !bioModal.hidden) {
      closeBioModal();
    }
  });
}

const closeVideoModal = () => {
  if (!videoModal) {
    return;
  }

  videoModal.hidden = true;
  body.classList.remove("modal-open");

  if (lastVideoTrigger) {
    lastVideoTrigger.focus();
  }
};

if (videoModal && videoTriggers.length) {
  const openVideoModal = (trigger) => {
    const styleName = trigger.dataset.videoStyle || "ce style";

    if (videoModalTitle) {
      videoModalTitle.textContent = styleName;
    }

    if (videoStyleName) {
      videoStyleName.textContent = styleName;
    }

    if (videoPlaceholderLabel) {
      videoPlaceholderLabel.textContent = `Extrait représentatif de ${styleName} à intégrer`;
    }

    lastVideoTrigger = trigger;
    videoModal.hidden = false;
    body.classList.add("modal-open");

    if (videoModalCloseButton) {
      videoModalCloseButton.focus();
    }
  };

  videoTriggers.forEach((trigger) => {
    trigger.addEventListener("click", () => openVideoModal(trigger));
  });

  videoCloseControls.forEach((control) => {
    control.addEventListener("click", closeVideoModal);
  });

  window.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !videoModal.hidden) {
      closeVideoModal();
    }
  });
}

const syncGlowParallax = () => {
  glowFrame = 0;

  if (!glowSurfaces.length) {
    return;
  }

  if (prefersReducedMotion.matches) {
    glowSurfaces.forEach((surface) => {
      surface.style.setProperty("--glow-left-x", "0px");
      surface.style.setProperty("--glow-left-y", "0px");
      surface.style.setProperty("--glow-right-x", "0px");
      surface.style.setProperty("--glow-right-y", "0px");
    });
    return;
  }

  const viewportHeight = window.innerHeight || 1;

  glowSurfaces.forEach((surface) => {
    const rect = surface.getBoundingClientRect();
    const passage = viewportHeight + rect.height;
    const progress = clamp((viewportHeight - rect.top) / passage, 0, 1);
    const centered = progress * 2 - 1;
    const arc = Math.sin(progress * Math.PI);
    const travelY = passage * 1.05;
    const leftY = lerp(-travelY, travelY, progress);
    const rightY = lerp(-travelY * 0.66, travelY * 0.9, progress) - arc * 70;
    const leftX = centered * 168;
    const rightX = centered * -129 + arc * 78;

    surface.style.setProperty("--glow-left-x", `${leftX}px`);
    surface.style.setProperty("--glow-left-y", `${leftY}px`);
    surface.style.setProperty("--glow-right-x", `${rightX}px`);
    surface.style.setProperty("--glow-right-y", `${rightY}px`);
  });
};

let glowFrame = 0;

const queueGlowParallax = () => {
  if (!glowSurfaces.length || glowFrame) {
    return;
  }

  glowFrame = window.requestAnimationFrame(syncGlowParallax);
};

if (glowSurfaces.length) {
  queueGlowParallax();

  window.addEventListener("scroll", queueGlowParallax, { passive: true });
  window.addEventListener("resize", queueGlowParallax);

  if (typeof prefersReducedMotion.addEventListener === "function") {
    prefersReducedMotion.addEventListener("change", queueGlowParallax);
  } else if (typeof prefersReducedMotion.addListener === "function") {
    prefersReducedMotion.addListener(queueGlowParallax);
  }
}

if ("IntersectionObserver" in window && !prefersReducedMotion.matches) {
  const revealedElements = document.querySelectorAll("[data-reveal]");

  if (revealedElements.length) {
    const observer = new IntersectionObserver(
      (entries, currentObserver) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add("is-visible");
            currentObserver.unobserve(entry.target);
          }
        });
      },
      {
        threshold: 0.18,
        rootMargin: "0px 0px -40px 0px"
      }
    );

    revealedElements.forEach((element) => observer.observe(element));
  }
} else {
  document.querySelectorAll("[data-reveal]").forEach((element) => {
    element.classList.add("is-visible");
  });
}
