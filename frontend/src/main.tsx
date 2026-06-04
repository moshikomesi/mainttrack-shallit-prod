import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { initDevAuth } from "./dev/devAuthBootstrap";
import App from "./App.tsx";
import "./index.css";

(async () => {
  await initDevAuth();
  createRoot(document.getElementById("root")!).render(
    <BrowserRouter>
      <App />
    </BrowserRouter>
  );
})();
