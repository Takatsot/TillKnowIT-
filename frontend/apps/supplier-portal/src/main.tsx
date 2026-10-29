import "@tillknowit/design-system/src/tokens.css";
import { SessionProvider, Shell } from "@tillknowit/app-kit";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { sections } from "./sections";

createRoot(document.getElementById("root") as HTMLElement).render(
  <StrictMode>
    <SessionProvider>
      <Shell title="TillKnowIT Supplier portal" sections={sections} />
    </SessionProvider>
  </StrictMode>,
);
