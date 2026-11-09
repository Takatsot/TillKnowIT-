import { configure } from "@testing-library/react";

// On a busy machine (or CI) a screen can take longer than the default one second to draw; waiting longer costs nothing when it is quick.
configure({ asyncUtilTimeout: 5000 });
