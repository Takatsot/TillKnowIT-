import { defineSection } from "@tillknowit/app-kit";
import { ProductsStock } from "./ProductsStock";
import { messages } from "./messages";

export const section = defineSection({ id: "products-stock", order: 10, title: "title", messages, needs: ["personId", "shopId"], component: ProductsStock });
