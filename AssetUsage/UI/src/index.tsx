import { ModRegistrar } from "cs2/modding";
import { LoadIssuesButton } from "./panel";

const register: ModRegistrar = (moduleRegistry) => {
    moduleRegistry.append("GameTopLeft", LoadIssuesButton);
};

export default register;
