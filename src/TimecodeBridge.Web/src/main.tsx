/** Browser composition root: installs bridge handling before mounting the Preact application. */
import { render } from "preact";
import { App } from "./components/app";
import { installBridge } from "./bridge";

installBridge();

const root = document.getElementById("app");
if (!root) throw new Error("Missing #app mount point");
render(<App />, root);
